using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime.Serialization.SystemTextJson;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Background loop that relays staged <see cref="OutboxMessage"/> rows onto
/// the MassTransit broker. Runs as an <see cref="IHostedService"/>; polls
/// the <c>outbox_messages</c> table every <see cref="OutboxOptions.ActivePollInterval"/>
/// when rows are present, and back-offs to <see cref="OutboxOptions.IdlePollInterval"/>
/// when the queue is empty.
///
/// Stops cleanly on shutdown after the in-flight batch completes. On
/// multi-replica deploys the dispatcher uses engine-native row-claim
/// hints (<c>SELECT FOR UPDATE SKIP LOCKED</c> on Postgres,
/// <c>WITH (ROWLOCK, UPDLOCK, READPAST)</c> on MSSQL) so two replicas
/// picking up the same row don't duplicate the publish — the second
/// replica's claim skips locked rows.
/// </summary>
public abstract class OutboxDispatcher<TContext> : BackgroundService
    where TContext : class, IOutboxDbContext
{
    private readonly IServiceProvider _services;
    private readonly OutboxOptions _options;
    private readonly ILogger _logger;

    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    }.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);

    protected OutboxDispatcher(
        IServiceProvider services,
        IOptions<OutboxOptions> options,
        ILogger logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Hook for the concrete service to resolve a fresh
    /// <typeparamref name="TContext"/> per poll iteration. The dispatch
    /// happens inside its own scope so a broker failure can be retried
    /// on the next tick without poisoning the caller's scope.</summary>
    protected abstract TContext CreateContext(IServiceProvider services);

    /// <summary>
    /// Engine-native SQL that claims up to <paramref name="batchSize"/>
    /// undispatched <see cref="OutboxMessage"/> rows AND locks them for
    /// the duration of the surrounding transaction. The base class runs
    /// the SELECT, the broker publish, and the dispatched-on stamp in
    /// the same transaction so the row's lock survives the publish.
    ///
    /// Implementations:
    /// <list type="bullet">
    /// <item>Postgres: appends <c>FOR UPDATE SKIP LOCKED</c>.</item>
    /// <item>MSSQL: appends <c>WITH (ROWLOCK, UPDLOCK, READPAST)</c>.</item>
    /// </list>
    /// </summary>
    protected abstract FormattableString BuildClaimSql(int batchSize);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Outbox dispatcher disabled via OutboxOptions.Enabled = false.");
            return;
        }

        _logger.LogInformation(
            "Outbox dispatcher started. Active poll {Active}s, idle poll {Idle}s, batch {Batch}.",
            _options.ActivePollInterval.TotalSeconds,
            _options.IdlePollInterval.TotalSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            int dispatched;
            try
            {
                dispatched = await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher iteration failed.");
                dispatched = 0;
            }

            var delay = dispatched > 0
                ? _options.ActivePollInterval
                : _options.IdlePollInterval;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox dispatcher stopped.");
    }

    /// <summary>
    /// Runs one dispatcher iteration out-of-band, bypassing the
    /// <see cref="OutboxOptions.Enabled"/> toggle and the polling
    /// delay. Used by tests to drive the dispatcher deterministically
    /// across two parallel replicas against the same backing store,
    /// proving that engine-native row locks prevent duplicate
    /// publishes when both replicas contend on the same row.
    /// </summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var dbContext = CreateContext(scope.ServiceProvider);
        var publishEndpoint = scope.ServiceProvider
            .GetRequiredService<IPublishEndpoint>();

        return await DispatchBatchAsync(dbContext, publishEndpoint, cancellationToken);
    }

    /// <summary>
    /// Claims a batch of undispatched rows under a transaction that
    /// holds row-level locks until each row is either dispatched (and
    /// stamped) or the transaction is rolled back. On multi-replica
    /// deploys, the second replica's claim sees a row that's locked by
    /// the first replica and skips it, eliminating duplicate publishes.
    /// </summary>
    private async Task<int> DispatchBatchAsync(
        TContext dbContext,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        // The claim + dispatch + stamp cycle runs inside one explicit
        // transaction so the engine-native row lock from
        // BuildClaimSql(...) holds until SaveChangesAsync commits.
        // Wrapped in Database.CreateExecutionStrategy().ExecuteAsync
        // so EnableRetryOnFailure(5, 10s) at the adopter's
        // UseNpgsql/UseSqlServer chain doesn't crash with
        // "The configured execution strategy ... does not support
        // user-initiated transactions". The wrapping
        // is a no-op for services without EnableRetryOnFailure but is
        // uniformly applied so the contract is identical across
        // adopters.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

            var pending = await ClaimPendingAsync(dbContext, ct);
            if (pending.Count == 0)
            {
                // Empty claim — commit (no-op transaction) before returning
                // so we don't leak a held connection.
                await tx.CommitAsync(ct);
                return 0;
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            foreach (var row in pending)
            {
                // Schema-version gate:
                // a future-version row wasn't yet known when this
                // dispatcher rolled out. Copy to outbox_messages_dead so an
                // operator can triage (bump MaxSupportedVersion after a new
                // consumer deploys) and skip the broker publish — the
                // destination consumer doesn't have the matching CLR type
                // yet.
                if (row.SchemaVersion > _options.MaxSupportedVersion)
                {
                    dbContext.OutboxDeadMessages.Add(new OutboxDeadMessage
                    {
                        Id = row.Id,
                        OccurredOn = row.OccurredOn,
                        Type = row.Type,
                        Payload = row.Payload,
                        SchemaVersion = row.SchemaVersion,
                        Reason = Reasons.UnsupportedSchemaVersion,
                        RejectedAt = now,
                    });
                    dbContext.OutboxMessages.Remove(row);

                    _logger.LogWarning(
                        "Outbox row {OutboxId} ({MessageType}) was stamped schema v{Schema} but the dispatcher only supports up to v{Max}. Quarantined to outbox_messages_dead.",
                        row.Id, row.Type, row.SchemaVersion, _options.MaxSupportedVersion);
                    continue;
                }

                try
                {
                    var messageType = Type.GetType(row.Type)
                        ?? throw new InvalidOperationException(
                            $"Outbox row {row.Id} references unknown type '{row.Type}'.");

                    var message = JsonSerializer.Deserialize(row.Payload, messageType, SerializerOptions)
                        ?? throw new InvalidOperationException(
                            $"Outbox row {row.Id} payload deserialized to null.");

                    await publishEndpoint.Publish(message, messageType, ct);
                    row.DispatchedAt = now;

                    _logger.LogDebug(
                        "Outbox row {OutboxId} ({MessageType}) dispatched at {DispatchedAt}.",
                        row.Id, row.Type, now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Outbox row {OutboxId} ({MessageType}) dispatch failed; will retry.",
                        row.Id, row.Type);
                    // Leave DispatchedAt null so the next tick retries.
                }
            }

            // EF sees the open transaction and won't auto-open its own;
            // SaveChanges flushes the row updates against the same
            // connection. The explicit commit releases the row locks.
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return pending.Count;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<OutboxMessage>> ClaimPendingAsync(
        TContext dbContext,
        CancellationToken cancellationToken)
    {
        // FromSql on a tracked DbSet so the stamp later routes through
        // the same change tracker — no second round-trip to re-fetch
        // the rows. The Engine-native SQL carries the row lock (see
        // BuildClaimSql) which is released when the surrounding
        // transaction commits in DispatchBatchAsync.
        return await dbContext.OutboxMessages
            .FromSql(BuildClaimSql(_options.BatchSize))
            .AsTracking()
            .ToListAsync(cancellationToken);
    }
}
