using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;
using Microsoft.Extensions.Options;

namespace Discount.Grpc.Messaging.Outbox;

/// <summary>
/// First SQLite <see cref="OutboxDispatcher{TContext}"/> implementation. Extends
/// the shared <c>OutboxDispatcher</c> base class with three service hooks:
///
/// <list type="bullet">
/// <item><see cref="CreateContext"/> — resolves a fresh <see cref="DiscountContext"/>
/// from the per-poll scope so a broker failure rolls back cleanly without
/// poisoning the surrounding request scope.</item>
/// <item><see cref="BuildClaimSql"/> — emits a SELECT of undispatched rows.
/// SQLite serializes writes via the database lock held by
/// <c>BeginTransactionAsync</c> in the base class; for single-replica
/// deployments this is sufficient.</item>
/// <item><see cref="ExecuteAsync"/> — overrides the base loop to add a
/// broker-circuit breaker per plan §6.7 v1.2 changelog M-L10. Counts
/// top-level <c>DispatchOnceAsync</c> throws (TX-commit failure, broker
/// unreachable, channel closed), writes the count to <see cref="BrokerHealthState"/>
/// for the <c>/ready</c> probe, pauses for
/// <c>OutboxOptions.BrokerBackoffSeconds</c> once tripped, and resets
/// on the first successful dispatch.</item>
/// </list>
///
/// <para>
/// The multi-replica <c>ClaimId</c>-based claiming pattern is
/// deferred to a follow-up. SQLite has no <c>SKIP LOCKED</c> equivalent; we
/// stage the column via a follow-up migration when HA is in scope.
/// </para>
/// </summary>
public sealed class DiscountOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<DiscountOutboxDispatcher> logger,
    BrokerHealthState brokerHealth)
    : OutboxDispatcher<DiscountContext>(services, options, logger)
{
    private readonly OutboxOptions _options = options.Value;
    private readonly ILogger<DiscountOutboxDispatcher> _logger = logger;
    private readonly BrokerHealthState _brokerHealth = brokerHealth;

    /// <inheritdoc />
    protected override DiscountContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<DiscountContext>();

    /// <inheritdoc />
    /// <remarks>
    /// SQLite claim SQL. The base class wraps the FromSql call in
    /// <c>BeginTransactionAsync</c>; the engine-level database lock held by
    /// that transaction prevents any other process from claiming the same
    /// rows in flight.
    /// </remarks>
    protected override FormattableString BuildClaimSql(int batchSize) =>
        $"SELECT * FROM outbox_messages WHERE DispatchedAt IS NULL ORDER BY OccurredOn ASC LIMIT {batchSize}";

    /// <inheritdoc />
    /// <remarks>
    /// Override of the base-class polling loop to add the broker circuit
    /// breaker. Reads <see cref="OutboxOptions.MaxConsecutiveBrokerFailures"/>
    /// and <see cref="OutboxOptions.BrokerBackoffSeconds"/> from the
    /// new fields added in Commit A (v1.6 changelog) — these live on
    /// <see cref="BuildingBlocks.Messaging.Outbox.OutboxOptions"/>.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox dispatcher disabled via OutboxOptions.Enabled = false.");
            return;
        }

        _logger.LogInformation(
            "Discount outbox dispatcher started. Active poll {Active}s, idle poll {Idle}s, batch {Batch}, breaker {Breaker} / {Backoff}s.",
            _options.ActivePollInterval.TotalSeconds,
            _options.IdlePollInterval.TotalSeconds,
            _options.BatchSize,
            _options.MaxConsecutiveBrokerFailures,
            _options.BrokerBackoffSeconds.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Pause once the breaker is tripped. The pause length is
            // BrokerBackoffSeconds (default 60s); the next iteration
            // re-attempts and either succeeds (counter resets) or fails
            // (counter increments, pause repeats).
            if (_brokerHealth.ConsecutiveBrokerFailures >= _options.MaxConsecutiveBrokerFailures)
            {
                _logger.LogWarning(
                    "Outbox broker circuit tripped ({Consecutive} consecutive failures). Backing off {Backoff}s.",
                    _brokerHealth.ConsecutiveBrokerFailures,
                    _options.BrokerBackoffSeconds.TotalSeconds);
                await BackoffAsync(_options.BrokerBackoffSeconds, stoppingToken).ConfigureAwait(false);
                continue;
            }

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
                // Top-level DispatchOnceAsync exception: count it,
                // update BrokerHealthState for the /ready probe,
                // backoff, then continue. The next iteration's
                // breaker gate may keep us paused if the count
                // crossed the threshold this tick.
                var failures = _brokerHealth.RecordFailure();
                _logger.LogError(
                    ex,
                    "Outbox dispatcher iteration failed ({Consecutive}/{Threshold} consecutive).",
                    failures,
                    _options.MaxConsecutiveBrokerFailures);
                await BackoffAsync(_options.BrokerBackoffSeconds, stoppingToken).ConfigureAwait(false);
                continue;
            }

            // Successful dispatch: reset the breaker counter and the
            // single-trip fail mark. Use Volatile-friendly writes.
            _brokerHealth.Reset();

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

        _logger.LogInformation("Discount outbox dispatcher stopped.");
    }

    /// <summary>
    /// Honoring-cancellation pause helper. Sleeps for
    /// <paramref name="delay"/> but throws
    /// <see cref="OperationCanceledException"/> promptly on
    /// <paramref name="stoppingToken"/> cancellation so the host
    /// shutdown phase never lingers for the full backoff window.
    /// </summary>
    private static async Task BackoffAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Swallow — the outer loop's cancellation gate handles it.
        }
    }
}
