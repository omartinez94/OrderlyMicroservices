namespace Ordering.Infrastructure.Caching;

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Ordering.Application.Dashboard.Dtos;

using Ordering.Application.Common.Interfaces;

public class DashboardSnapshotCache(
    IDistributedCache cache,
    ILogger<DashboardSnapshotCache> logger,
    IDashboardNotifier? notifier = null)
    : IDashboardSnapshotCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
        .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> PendingInvalidations = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> PendingSections = new();

    public async Task<RestaurantOperationsDashboardDto> GetOrComputeAsync(
        Guid restaurantId,
        bool includeRevenue,
        Func<Task<RestaurantOperationsDashboardDto>> computeFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computeFactory);

        string cacheKey = GetCacheKey(restaurantId);

        // 1. Try get from cache
        try
        {
            string? cachedJson = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                RestaurantOperationsDashboardDto? cachedSnapshot = JsonSerializer.Deserialize<RestaurantOperationsDashboardDto>(cachedJson, JsonOptions);
                if (cachedSnapshot is not null)
                {
                    return Redact(cachedSnapshot, includeRevenue);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read dashboard cache for restaurant {RestaurantId}; bypassing cache", restaurantId);
        }

        // 2. Single-flight lock per restaurant
        SemaphoreSlim gate = Locks.GetOrAdd(restaurantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Double check cache after acquiring lock
            try
            {
                string? cachedJson = await cache.GetStringAsync(cacheKey, cancellationToken);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    RestaurantOperationsDashboardDto? cachedSnapshot = JsonSerializer.Deserialize<RestaurantOperationsDashboardDto>(cachedJson, JsonOptions);
                    if (cachedSnapshot is not null)
                    {
                        return Redact(cachedSnapshot, includeRevenue);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read dashboard cache after lock acquisition for restaurant {RestaurantId}", restaurantId);
            }

            // 3. Compute raw (revenue-inclusive) snapshot
            RestaurantOperationsDashboardDto rawSnapshot = await computeFactory();

            // 4. Save to Redis
            try
            {
                string json = JsonSerializer.Serialize(rawSnapshot, JsonOptions);
                DistributedCacheEntryOptions entryOptions = new()
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                };
                await cache.SetStringAsync(cacheKey, json, entryOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write dashboard cache for restaurant {RestaurantId}", restaurantId);
            }

            return Redact(rawSnapshot, includeRevenue);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task InvalidateAsync(
        Guid restaurantId,
        IReadOnlyList<string>? changedSections = null,
        CancellationToken cancellationToken = default)
    {
        if (changedSections is not null && changedSections.Count > 0)
        {
            ConcurrentBag<string> bag = PendingSections.GetOrAdd(restaurantId, _ => new ConcurrentBag<string>());
            foreach (string section in changedSections)
            {
                bag.Add(section);
            }
        }

        // Debounce invalidation by 2 seconds to avoid cache thrash during burst events
        CancellationTokenSource newCts = new();
        CancellationTokenSource? oldCts = PendingInvalidations.AddOrUpdate(
            restaurantId,
            newCts,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return newCts;
            });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, newCts.Token);
                string cacheKey = GetCacheKey(restaurantId);
                await cache.RemoveAsync(cacheKey, CancellationToken.None);
                PendingInvalidations.TryRemove(restaurantId, out _);

                List<string> sections = PendingSections.TryRemove(restaurantId, out ConcurrentBag<string>? b)
                    ? b.Distinct().ToList()
                    : [];

                if (sections.Count == 0)
                {
                    sections = ["meta", "ticker", "salesPace", "kitchenVelocity", "floorPulse", "openChecks", "waitlist", "alerts"];
                }

                logger.LogInformation("Dashboard cache invalidated for restaurant {RestaurantId}", restaurantId);

                if (notifier is not null)
                {
                    await notifier.NotifyDashboardChangedAsync(restaurantId, sections, CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                // Debounced by a subsequent invalidation request
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to invalidate dashboard cache for restaurant {RestaurantId}", restaurantId);
            }
        });

        return Task.CompletedTask;
    }

    public static RestaurantOperationsDashboardDto Redact(
        RestaurantOperationsDashboardDto raw,
        bool includeRevenue)
    {
        if (includeRevenue)
        {
            return raw;
        }

        LiveOperationalTickerDto? ticker = raw.Ticker is null
            ? null
            : raw.Ticker with
            {
                NetSalesToday = null,
                SalesTargetToday = null,
                SalesGrowthPercentage = null,
            };

        SalesPaceDto? salesPace = raw.SalesPace is null
            ? null
            : raw.SalesPace with
            {
                OpenBalanceTotal = null,
                DiscountsTotal = null,
                DiscountPercentageOfSales = null,
                CashTenderedTotal = null,
                DigitalTenderedTotal = null,
                HourlySales = raw.SalesPace.HourlySales
                    .Select(h => h with { GrossSales = null, HistoricalAverageSales = null })
                    .ToList(),
            };

        IReadOnlyList<OpenCheckDto> openChecks = raw.OpenChecks
            .Select(c => c with { Balance = null })
            .ToList();

        return raw with
        {
            Ticker = ticker,
            SalesPace = salesPace,
            OpenChecks = openChecks,
        };
    }

    private static string GetCacheKey(Guid restaurantId) =>
        $"restaurant:{restaurantId:D}:operations:live_summary";
}
