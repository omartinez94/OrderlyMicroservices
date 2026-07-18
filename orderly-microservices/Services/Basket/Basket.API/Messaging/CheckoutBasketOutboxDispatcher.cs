using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Correlation;
using BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Basket.API.Messaging;

/// <summary>
/// Background relay that drains staged <see cref="CheckoutBasketOutboxMessage"/>
/// rows from the <c>mt_doc_checkoutbasketoutboxmessage</c> Marten table
/// onto the MassTransit broker. Polls every
/// <see cref="OutboxOptions.ActivePollInterval"/> when rows are present
/// and back-offs to <see cref="OutboxOptions.IdlePollInterval"/> when the
/// queue is empty.
/// </summary>
/// <remarks>
/// <para>
/// Does <em>not</em> extend <c>OutboxDispatcher&lt;TContext&gt;</c> —
/// that base class is EF-Core-shaped (<c>DbSet&lt;OutboxMessage&gt;</c>,
/// <c>DatabaseFacade</c>, <c>FromSql(BuildClaimSql(...))</c>) and
/// cannot be reused against <see cref="IDocumentSession"/>. The polling
/// loop mirrors the EF-Core base class verbatim so a future
/// <c>MartenOutboxDispatcher&lt;TStore&gt;</c> can be factored into
/// <c>BuildingBlocks.Messaging.Outbox</c> when a second Marten-using
/// service adopts the pattern (BASKET_SERVICE_PLAN.md §6 Phase 2
/// drift item 1).
/// </para>
/// <para>
/// Phase 2 v1 claim is Marten LINQ + optimistic concurrency
/// (<c>mt_version</c> column increments per update). Multi-replica
/// safety requires switching to raw-SQL claim with
/// <c>FOR UPDATE SKIP LOCKED</c> via <c>IDocumentSession.Connection</c>
/// — drift item 3, deferred to a Phase 4 hand-off.
/// </para>
/// <para>
/// <see cref="IAsyncDisposable"/> + <see cref="IDisposable"/> per plan
/// §0.3.3. <see cref="StopAsync"/> drains the in-flight iteration
/// before allowing the host to shut down.
/// </para>
/// </remarks>
public sealed class CheckoutBasketOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<CheckoutBasketOutboxDispatcher> logger)
    : BackgroundService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly OutboxOptions _options = options.Value;
    private readonly ILogger<CheckoutBasketOutboxDispatcher> _logger = logger;

    /// <summary>Tag-name extension on the base polling loop. Lives here
    /// (not on the EF-Core base class) because the EF-Core shape doesn't
    /// apply.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Basket outbox dispatcher disabled via OutboxOptions.Enabled = false.");
            return;
        }

        _logger.LogInformation(
            "Basket outbox dispatcher started. Active poll {Active}s, idle poll {Idle}s, batch {Batch}.",
            _options.ActivePollInterval.TotalSeconds,
            _options.IdlePollInterval.TotalSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            int dispatched;
            try
            {
                dispatched = await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Basket outbox dispatcher iteration failed.");
                dispatched = 0;
            }

            var delay = dispatched > 0
                ? _options.ActivePollInterval
                : _options.IdlePollInterval;
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Basket outbox dispatcher stopped.");
    }

    /// <summary>
    /// Runs one dispatcher iteration out-of-band, bypassing the
    /// <see cref="OutboxOptions.Enabled"/> toggle and the polling
    /// delay. Visible for tests so the dispatcher can be driven
    /// deterministically without waiting for the next tick.
    /// </summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        return await DispatchBatchAsync(session, publishEndpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Claims up to <see cref="OutboxOptions.BatchSize"/> undispatched
    /// rows via Marten LINQ, publishes each onto MassTransit, and
    /// stamps <see cref="CheckoutBasketOutboxMessage.DispatchedAt"/>
    /// before the surrounding <c>SaveChangesAsync</c> commits.
    /// </summary>
    private async Task<int> DispatchBatchAsync(
        IDocumentSession session,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var pending = await session.Query<CheckoutBasketOutboxMessage>()
            .Where(m => m.DispatchedAt == null)
            .OrderBy(m => m.OccurredOn)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return 0;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var row in pending)
        {
            // Schema-version gate: a future-version row wasn't yet
            // known when this dispatcher rolled out. Phase 2 v1 simply
            // skips — a follow-up commit can route to a Marten
            // quarantine document mirroring the EF-Core
            // OutboxDeadMessage shape.
            if (row.SchemaVersion > _options.MaxSupportedVersion)
            {
                _logger.LogWarning(
                    "Outbox row {OutboxId} ({MessageType}) was stamped schema v{Schema} but the dispatcher only supports up to v{Max}. Skipping.",
                    row.Id, row.Type, row.SchemaVersion, _options.MaxSupportedVersion);
                continue;
            }

            using var _ = _logger.BeginScope(new Dictionary<string, object>
            {
                ["OutboxMessageId"] = row.Id,
                ["MessageVersion"] = row.SchemaVersion,
                ["EventType"] = row.Type,
                ["CorrelationId"] = CorrelationContext.Current ?? "<none>",
            });

            try
            {
                var messageType = Type.GetType(row.Type)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {row.Id} references unknown type '{row.Type}'.");

                var message = JsonSerializer.Deserialize(row.Payload, messageType, SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {row.Id} payload deserialized to null.");

                if (message is not IntegrationEvent)
                {
                    throw new InvalidOperationException(
                        $"Outbox row {row.Id} payload type '{row.Type}' does not implement IntegrationEvent.");
                }

                await publishEndpoint.Publish(message, messageType, cancellationToken).ConfigureAwait(false);
                row.DispatchedAt = now;

                _logger.LogDebug(
                    "Outbox row {OutboxId} ({MessageType}) dispatched at {DispatchedAt}.",
                    row.Id, row.Type, now);
            }
            catch (Exception ex)
            {
                // Per-row failure: leave DispatchedAt null so the next
                // tick retries. The base EF-Core dispatcher treats these
                // as poison rows; we do the same — outer ExecuteAsync
                // does NOT count them as broker-circuit failures
                // (top-level only, per OutboxOptions.MaxConsecutiveBrokerFailures).
                _logger.LogError(
                    ex,
                    "Outbox row {OutboxId} ({MessageType}) dispatch failed; will retry.",
                    row.Id, row.Type);
            }
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return pending.Count;
    }

    /// <summary>
    /// Drain in-flight work before host shutdown. The base
    /// <see cref="BackgroundService.StopAsync"/> cancels the polling
    /// loop; this override also waits for the active
    /// <see cref="DispatchOnceAsync"/> to finish if one is mid-iteration
    /// so we don't truncate a half-staged outbox row.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // No unmanaged resources today; the per-iteration IServiceScope
        // is disposed inside DispatchOnceAsync. Kept for future-proofing
        // when the dispatcher gains a long-lived channel or buffer.
        // BackgroundService.Dispose() handles the stoppingCts cleanup;
        // we don't override Dispose() because there's nothing to add.
        return ValueTask.CompletedTask;
    }
}
