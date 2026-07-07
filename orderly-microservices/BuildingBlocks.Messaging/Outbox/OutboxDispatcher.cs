using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Background loop that relays staged <see cref="OutboxMessage"/> rows onto
/// the MassTransit broker. Runs as an <see cref="IHostedService"/>; polls
/// the <c>outbox_messages</c> table every <see cref="OutboxOptions.ActivePollInterval"/>
/// when rows are present, and back-offs to <see cref="OutboxOptions.IdlePollInterval"/>
/// when the queue is empty.
///
/// Stops cleanly on shutdown after the in-flight batch completes (no
/// message is half-dispatched on crash — the row stays unclaimed and the
/// next process picks it up).
/// </summary>
public abstract class OutboxDispatcher<TContext> : BackgroundService
    where TContext : class, IOutboxDbContext
{
    private readonly IServiceProvider _services;
    private readonly OutboxOptions _options;
    private readonly ILogger _logger;

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
                using var scope = _services.CreateScope();
                var dbContext = CreateContext(scope.ServiceProvider);
                var publishEndpoint = scope.ServiceProvider
                    .GetRequiredService<IPublishEndpoint>();

                dispatched = await DispatchBatchAsync(
                    dbContext, publishEndpoint, stoppingToken);
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
    /// Claims up to <see cref="OutboxOptions.BatchSize"/> undispatched rows
    /// and publishes them via MassTransit. On success, stamps
    /// <see cref="OutboxMessage.DispatchedAt"/>. Errors are logged but do
    /// not stamp the row, so the next iteration retries.
    /// </summary>
    private async Task<int> DispatchBatchAsync(
        TContext dbContext,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.OutboxMessages
            .Where(m => m.DispatchedAt == null)
            .OrderBy(m => m.OccurredOn)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return 0;

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var row in pending)
        {
            try
            {
                var messageType = Type.GetType(row.Type)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {row.Id} references unknown type '{row.Type}'.");

                var message = JsonSerializer.Deserialize(row.Payload, messageType)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {row.Id} payload deserialized to null.");

                await publishEndpoint.Publish(message, messageType, cancellationToken);
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

        await dbContext.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}