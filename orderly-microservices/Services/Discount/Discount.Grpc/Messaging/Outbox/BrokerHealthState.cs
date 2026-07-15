namespace Discount.Grpc.Messaging.Outbox;

/// <summary>
/// Mutable singleton that tracks the dispatcher's broker-circuit state.
/// Written by <see cref="DiscountOutboxDispatcher"/> when top-level
/// <c>DispatchOnceAsync</c> throws (TX-commit failure, broker
/// unreachable, channel closed) or when a successful dispatch resets
/// the counter. Read by the <c>broker-circuit</c> readiness probe in
/// <c>DiscountHealthChecks.cs</c> so the <c>/ready</c> endpoint flips to
/// <c>Unhealthy</c> when the breaker is tripped.
/// </summary>
/// <remarks>
/// <para>The counter increments <em>only</em> on top-level
/// <see cref="OutboxDispatcher{TContext}.DispatchOnceAsync"/>
/// throws — per-row publish failures inside <c>DispatchBatchAsync</c>
/// are poison rows and stay local to that batch (see plan §6.7 v1.2
/// M-L10 rationale).</para>
/// <para>Reads use <see cref="Volatile.Read"/> on <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
/// to fence against reordering; writes use
/// <see cref="Interlocked.Exchange(ref int, int)"/> so concurrent
/// resets don't drop increments. <see cref="TrippedAt"/> is set on
/// the same thread that observes the trip, so its value is
/// best-effort accurate (racy under concurrent resets, but only the
/// value matters for the alert — last-trip timestamp).</para>
/// </remarks>
public sealed class BrokerHealthState
{
    private int _consecutiveBrokerFailures;
    private long _trippedAtTicks; // 0 == not tripped; DateTimeOffset.ToUnixTimeMilliseconds otherwise.

    /// <summary>
    /// Current consecutive-failure count (0 means "no recent failures").
    /// Thread-safe read.
    /// </summary>
    public int ConsecutiveBrokerFailures =>
        Volatile.Read(ref _consecutiveBrokerFailures);

    /// <summary>
    /// When the counter was last observed at-or-above the threshold.
    /// <c>null</c> when the circuit has never tripped in this process
    /// lifetime. Best-effort accurate under concurrent resets.
    /// </summary>
    public DateTimeOffset? TrippedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _trippedAtTicks);
            return ticks == 0
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(ticks);
        }
    }

    /// <summary>
    /// Increment the counter on a top-level broker failure. Returns the
    /// post-increment value so the dispatcher can log it.
    /// </summary>
    public int RecordFailure()
    {
        var next = Interlocked.Increment(ref _consecutiveBrokerFailures);
        // Stamp the tripped-at timestamp the first time the counter
        // crosses 0; subsequent failures don't overwrite the original
        // trip clock. The dispatcher's IsHealthy(threshold) check
        // decides whether the current count is actually tripping the
        // breaker; that decision is the dispatcher's call, not this
        // method's.
        if (next == 1)
        {
            Interlocked.Exchange(
                ref _trippedAtTicks,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        return next;
    }

    /// <summary>
    /// Reset the counter on a successful dispatch. The tripped-at
    /// timestamp is preserved for ops dashboards — clearing it would
    /// erase history. Reads <see cref="ILogger"/> as the natural
    /// future-side companion for a "circuit reset" log line, but the
    /// log lives in the dispatcher (which has the structured scope).
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _consecutiveBrokerFailures, 0);
    }

    /// <summary>
    /// True while the failure counter is below the configured
    /// threshold. Mirrors the dispatcher's pause-or-proceed decision.
    /// </summary>
    public bool IsHealthy(int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threshold);
        return ConsecutiveBrokerFailures < threshold;
    }
}
