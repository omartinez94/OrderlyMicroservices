namespace Basket.API.Services;

/// <summary>
/// Stable handle for the <see cref="BasketExpirySweepService.SweepOnceAsync"/>
/// method so the dev-only <c>/_dev/trigger/clear-abandoned-baskets</c>
/// endpoint can drive the sweep without binding to the concrete
/// <see cref="BackgroundService"/> type. The endpoint is registered
/// only when <c>app.Environment.IsDevelopment()</c> is true.
/// </summary>
public interface IBasketExpirySweepRunner
{
    /// <summary>
    /// Runs one sweep iteration out-of-band, bypassing the
    /// <see cref="BasketOptions.ExpirySweepOptions.Enabled"/> toggle
    /// and the periodic timer.
    /// </summary>
    /// <returns>
    /// The number of baskets deleted by this iteration. Zero when
    /// no rows are expired.
    /// </returns>
    Task<int> SweepOnceAsync(CancellationToken cancellationToken);
}