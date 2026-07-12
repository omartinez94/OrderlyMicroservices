using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Scheduling;

/// <summary>
/// Hangfire recurring job: keeps <see cref="MenuItem.IsAvailable"/> in
/// sync with the seasonal / promo date windows for items whose
/// <see cref="ItemType"/> is <c>Seasonal</c> or <c>Promo</c>. Self-gates
/// on <c>CatalogScheduledJobs</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Seasonal</b>: <see cref="MenuItem.IsAvailable"/> ← <c>SeasonStartDate ≤ today ≤ SeasonEndDate</c>.</item>
///   <item><b>Promo</b>: <see cref="MenuItem.IsAvailable"/> ← <c>PromoStartDate ≤ now ≤ PromoEndDate</c>.</item>
/// </list>
/// Cadence is configurable via <see cref="HangfireOptions.SeasonalAvailabilityCron"/>
/// (default every 5 minutes). The engine's domain-event path is still the
/// authoritative source for ingredient-driven availability flips; this job
/// only flips <c>IsAvailable</c> based on calendar windows — it does not
/// publish a <c>MenuItemChanged</c> event (the read-side cache layer picks
/// up the value on the next menu-tree read; this job only changes
/// <c>IsAvailable</c>, not the cached tree shape).
/// </remarks>
public sealed class SeasonalAvailabilityJob(
    IServiceScopeFactory scopeFactory,
    IFeatureManager featureManager,
    TimeProvider clock,
    IOptions<HangfireOptions> options,
    ILogger<SeasonalAvailabilityJob> logger)
{
    private readonly HangfireOptions _options = options.Value;

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync("CatalogScheduledJobs", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var now = clock.GetUtcNow();
        var nowInstant = Instant.FromDateTimeUtc(now.UtcDateTime);
        var today = LocalDate.FromDateTime(now.UtcDateTime);

        // Pre-load candidate items so the actual loop is allocation-free.
        var candidates = await dbContext.MenuItems
            .Where(m => !m.IsDeleted && (m.ItemType == ItemType.Seasonal || m.ItemType == ItemType.Promo))
            .OrderBy(m => m.Id)
            .Take(_options.MaxRowsPerTick)
            .ToListAsync(cancellationToken);

        var flipped = 0;
        foreach (var item in candidates)
        {
            var shouldBeAvailable = item.ItemType switch
            {
                ItemType.Seasonal => IsWithinSeasonalWindow(item, today),
                ItemType.Promo => IsWithinPromoWindow(item, nowInstant),
                _ => item.IsAvailable,
            };

            if (item.IsAvailable != shouldBeAvailable)
            {
                item.IsAvailable = shouldBeAvailable;
                flipped++;
            }
        }

        if (flipped > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "SeasonalAvailabilityJob flipped availability on {Count} items", flipped);
        }
    }

    private static bool IsWithinSeasonalWindow(MenuItem item, LocalDate today)
    {
        if (item.SeasonStartDate is null || item.SeasonEndDate is null)
        {
            // Incomplete seasonal definition — leave whatever was there.
            return item.IsAvailable;
        }

        return today >= item.SeasonStartDate.Value && today <= item.SeasonEndDate.Value;
    }

    private static bool IsWithinPromoWindow(MenuItem item, Instant now)
    {
        if (item.PromoStartDate is null || item.PromoEndDate is null)
        {
            return item.IsAvailable;
        }

        return now >= item.PromoStartDate.Value && now <= item.PromoEndDate.Value;
    }
}