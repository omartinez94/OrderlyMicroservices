using Catalog.API.Caching;
using Catalog.API.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Catalog.API.Health;

/// <summary>
/// Reads the <c>outbox_messages_dead</c> row count and reports
/// <see cref="HealthStatus.Unhealthy"/> when it exceeds
/// <see cref="CatalogOptions.OutboxDeadLetterThreshold"/>. Registered as
/// the <c>outbox_dlq</c> check on the <c>/ready</c> endpoint (per
/// <c>CATALOG_SERVICE_PLAN.md</c> §7 Phase 2 health-check spec).
/// </summary>
/// <remarks>
/// <para><b>Scope per tick.</b> Resolves a fresh
/// <see cref="IServiceScope"/> so the scoped <see cref="CatalogDbContext"/>
/// is disposed cleanly between probes — the probe can be invoked on a
/// tight cadence without leaking context instances.</para>
/// <para><b>Fail-open.</b> A probe failure (e.g. transient Postgres outage)
/// returns <see cref="HealthStatus.Unhealthy"/> with the exception attached;
/// it does not silently swallow errors. The <c>/ready</c> endpoint surfaces
/// this as 503, pulling the replica out of the load balancer.</para>
/// </remarks>
public sealed class OutboxDeadLetterProbe(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CatalogOptions> options,
    ILogger<OutboxDeadLetterProbe> logger) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            // Fully-qualified EF Core CountAsync — `System.Linq.Async`'s CountAsync
            // shadows the IQueryable<T> overload with a (predicate, cancellationToken)
            // signature when the package is transitively present, which would otherwise
            // make this call ambiguous.
            var count = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(dbContext.OutboxDeadMessages, cancellationToken)
                .ConfigureAwait(false);
            var threshold = options.CurrentValue.OutboxDeadLetterThreshold;

            var data = new Dictionary<string, object>
            {
                ["dead_message_count"] = count,
                ["threshold"] = threshold,
            };

            if (count > threshold)
            {
                logger.LogError(
                    "Outbox dead-message count {Count} exceeds threshold {Threshold}; /ready will return 503.",
                    count,
                    threshold);
                return HealthCheckResult.Unhealthy(
                    $"Dead-letter count {count} > threshold {threshold}",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Dead-letter count {count} ≤ threshold {threshold}",
                data: data);
        }
        catch (Exception ex)
        {
            // Don't swallow — surface as Unhealthy so /ready returns 503.
            logger.LogError(ex, "OutboxDeadLetterProbe failed; /ready will return 503.");
            return HealthCheckResult.Unhealthy("Outbox dead-letter probe threw.", ex);
        }
    }
}