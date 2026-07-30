namespace Ordering.Infrastructure;

/// <summary>
/// Stable handle for the ordering-side outbox dispatcher so the
/// dev-only <c>/_dev/trigger/outbox-relay</c> endpoint can drive
/// a one-shot dispatch without binding to the concrete
/// <see cref="BackgroundService"/> type. Mirrors the
/// <c>IBasketExpirySweepRunner</c> pattern from the Basket.API.
/// </summary>
public interface IOrderingOutboxRunner
{
    /// <summary>
    /// Runs one outbox-dispatch iteration out-of-band, bypassing
    /// the periodic timer and the broker-circuit breaker state.
    /// </summary>
    /// <returns>
    /// The number of outbox rows dispatched by this iteration.
    /// </returns>
    Task<int> DispatchOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Daily reconciliation runner: orders in <c>Confirmed</c> /
/// <c>Preparing</c> / <c>Ready</c> whose parent <c>OrderStatus</c>
/// has drifted (e.g. card-payment failure, abandoned cart upstream)
/// are reconciled against the latest catalog state. Today the
/// project has no scheduler wiring for this; the dev-only
/// <c>/_dev/trigger/daily-reconciliation</c> endpoint is the
/// operator's manual hook. When a real Hangfire job lands, the
/// interface stays — the Hangfire wrapper delegates to
/// <see cref="RunAsync"/>.
/// </summary>
public interface IDailyReconciliationRunner
{
    /// <summary>
    /// Runs the daily reconciliation pass.
    /// </summary>
    /// <returns>The number of orders reconciled.</returns>
    Task<int> RunAsync(CancellationToken cancellationToken);
}