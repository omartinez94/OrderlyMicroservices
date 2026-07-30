using Microsoft.Extensions.Logging;

namespace Ordering.Infrastructure.Services;

/// <summary>
/// Placeholder implementation of <see cref="IDailyReconciliationRunner"/>.
/// The reconciliation logic (re-checking catalog state, recovering
/// orphaned orders, recomputing bill totals against the latest
/// menu) is not in this plan's scope — the dev-only
/// <c>/_dev/trigger/daily-reconciliation</c> endpoint exists so the
/// MCP server's <c>trigger_scheduled_jobs</c> tool has a target.
/// </summary>
/// <remarks>
/// The real implementation lands with the future ordering-scheduler
/// plan (Hangfire wiring + the actual reconciliation algorithm).
/// This stub returns <c>0</c> immediately so the endpoint stays
/// green; when the real implementation lands, the MCP server
/// continues to call the same path unchanged.
/// </remarks>
public sealed class DailyReconciliationRunner(
    ILogger<DailyReconciliationRunner> logger)
    : IDailyReconciliationRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Smoke-check: log so the operator sees the dev trigger fired.
        logger.LogInformation(
            "Daily reconciliation triggered via dev endpoint (placeholder implementation; no-op).");

        // Real implementation would query `Orders` for status drift
        // and reconcile against `MenuItems`. Out of scope for the
        // Dev MCP close pass.
        await Task.CompletedTask;
        return 0;
    }
}