using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;

namespace Catalog.API.Caching;

/// <summary>
/// Background hosted service that periodically re-populates missing
/// <c>catalog:menu:{rid}</c> cache entries. Runs every
/// <see cref="CatalogOptions.CacheRepairIntervalMinutes"/> minutes (default 5).
/// </summary>
/// <remarks>
/// <para><b>Why DB → cache instead of Redis SCAN.</b> The decorator uses
/// <see cref="IDistributedCache"/>, which has no key-enumeration API. Listing
/// keys requires <c>IConnectionMultiplexer</c> (out of scope for Phase 1 by
/// architectural decision). The drift-repair service therefore enumerates
/// <em>restaurants in the DB</em> — distinct ids of <c>MenuCategories</c> —
/// and checks each one's cache key. This also keeps the cache scope to
/// "restaurants that should have a snapshot", avoiding orphaned keys.</para>
/// <para><b>Feature flag.</b> The tick self-gates on
/// <c>FeatureManagement__CatalogRedisCache</c>. Disabling the flag in
/// production stops the service from doing any work; the cache key removal
/// in <see cref="ICatalogCache"/> still happens (best-effort) but the repair
/// loop is dormant.</para>
/// <para><b>Captive dependencies.</b> <see cref="IDistributedCache"/>,
/// <see cref="IFeatureManager"/>, and <see cref="IOptionsMonitor{T}"/> are
/// singletons — safe to inject directly. <see cref="CatalogDbContext"/> and
/// <see cref="IMenuReader"/> are <c>Scoped</c>, so the service resolves them
/// per tick via <see cref="IServiceScopeFactory"/>.</para>
/// <para><b>Failure policy.</b> Per-restaurant failures are logged at
/// <c>Warning</c> and skipped — one bad restaurant does not stop the tick.
/// The whole tick wraps in a try/catch so an unexpected exception is logged
/// and the loop continues to the next interval.</para>
/// </remarks>
public sealed class CacheDriftRepairService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CatalogOptions> options,
    IFeatureManager featureManager,
    ILogger<CacheDriftRepairService> logger) : BackgroundService
{
    /// <summary>
    /// The hosted service loop. Polls on a coarse-grained timer (re-evaluated
    /// after each tick so config changes take effect without a restart), then
    /// runs <see cref="RepairDriftAsync"/> when the feature flag is enabled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CacheDriftRepairService starting (default interval: {IntervalMinutes}m).",
            options.CurrentValue.CacheRepairIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await featureManager.IsEnabledAsync("CatalogRedisCache", stoppingToken).ConfigureAwait(false))
                {
                    await RepairDriftAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    logger.LogDebug("CatalogRedisCache flag is off; skipping drift-repair tick.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                // Fail-open: the loop must survive. We log and continue.
                logger.LogError(
                    new CacheRepairFailedException("Drift-repair tick failed", ex),
                    "CacheDriftRepairService tick failed; will retry on next interval.");
            }

            try
            {
                var interval = TimeSpan.FromMinutes(options.CurrentValue.CacheRepairIntervalMinutes);
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("CacheDriftRepairService stopped.");
    }

    /// <summary>
    /// Runs one drift-repair tick: enumerates restaurants, checks each
    /// <c>catalog:menu:{rid}</c> cache key, repopulates any that are missing.
    /// </summary>
    private async Task RepairDriftAsync(CancellationToken cancellationToken)
    {
        // Scope per tick so we don't capture a Scoped DbContext on the Singleton
        // background service (see plan §0.3.3 captive dependencies).
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IMenuReader>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var restaurantIds = await dbContext.MenuCategories
            .AsNoTracking()
            .Select(c => c.RestaurantId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (restaurantIds.Count == 0)
        {
            logger.LogDebug("Drift-repair tick: no restaurants in DB; nothing to do.");
            return;
        }

        var ttl = TimeSpan.FromMinutes(options.CurrentValue.MenuCacheTtlMinutes);
        var repaired = 0;
        var inspected = 0;

        foreach (var restaurantId in restaurantIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            inspected++;

            string? existing;
            try
            {
                existing = await cache.GetStringAsync(CacheKeys.Menu(restaurantId), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Drift-repair: cache read failed for restaurant {RestaurantId}; skipping.",
                    restaurantId);
                continue;
            }

            if (!string.IsNullOrEmpty(existing))
            {
                continue;
            }

            try
            {
                var snapshot = await reader.GetByRestaurantAsync(restaurantId, cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is null)
                {
                    continue;
                }

                var payload = System.Text.Json.JsonSerializer.Serialize(snapshot);
                await cache.SetStringAsync(
                    CacheKeys.Menu(restaurantId),
                    payload,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                    cancellationToken).ConfigureAwait(false);
                repaired++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Drift-repair: repopulate failed for restaurant {RestaurantId}; will retry on next tick.",
                    restaurantId);
            }
        }

        logger.LogInformation(
            "Drift-repair tick: inspected {Inspected} restaurants, repopulated {Repaired}.",
            inspected,
            repaired);
    }
}