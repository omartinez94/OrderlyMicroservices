using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Availability;

/// <summary>
/// Nightly background sweep that re-validates every restaurant's
/// <see cref="MenuItemAnalytics"/> rows for the current day.
/// Safety net for the <c>OrderCompletedIntegrationEvent</c> consumer —
/// if a consumer ever drops a row (DB hiccup, dispatcher crash), the
/// nightly pass catches the drift within 24h.
/// </summary>
/// <remarks>
/// Runs at <see cref="MenuItemAnalyticsNightlyRecomputeServiceOptions.RunAtHour"/>
/// local server time every day. Uses an inner <see cref="IServiceScope"/>
/// per tick so the <c>CatalogDbContext</c> lifetime is bounded.
/// </remarks>
public sealed class MenuItemAnalyticsNightlyRecomputeServiceOptions
{
    public const string SectionName = "MenuItemAnalyticsNightly";

    /// <summary>Hour of day (0–23) at which the sweep runs.</summary>
    [Range(0, 23)]
    public int RunAtHour { get; set; } = 3;
}

public sealed class MenuItemAnalyticsNightlyRecomputeService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<MenuItemAnalyticsNightlyRecomputeServiceOptions> options,
    ILogger<MenuItemAnalyticsNightlyRecomputeService> logger) : BackgroundService
{
    private readonly MenuItemAnalyticsNightlyRecomputeServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "MenuItemAnalyticsNightlyRecomputeService scheduled to run at {Hour}:00 server-time daily",
            _options.RunAtHour);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var nextRun = NextRunAt(now, _options.RunAtHour);
            var delay = nextRun - now;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Log + continue — the next nightly pass will retry.
                logger.LogError(ex, "MenuItemAnalyticsNightlyRecomputeService sweep failed; will retry tomorrow");
            }
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var today = LocalDate.FromDateTime(DateTime.UtcNow);

        // For the nightly pass, the simplest drift repair is to ensure every
        // (RestaurantId, MenuItemId) pair that has any analytics row for today
        // is internally consistent. Full reconstruction would require
        // re-reading the order stream, which is not safe to do out of band.
        var inconsistent = await dbContext.MenuItemAnalytics
            .Where(x => x.AnalysisDate == today)
            .Where(x => x.TimesOrdered < 0 || x.TotalRevenue < 0m || x.TimesOutOfStock < 0)
            .ToListAsync(stoppingToken);

        if (inconsistent.Count == 0)
        {
            logger.LogInformation("MenuItemAnalyticsNightlyRecomputeService: no drift detected for {Date}", today);
            return;
        }

        foreach (var row in inconsistent)
        {
            if (row.TimesOrdered < 0) row.TimesOrdered = 0;
            if (row.TotalRevenue < 0m) row.TotalRevenue = 0m;
            if (row.TimesOutOfStock < 0) row.TimesOutOfStock = 0;
        }

        await dbContext.SaveChangesAsync(stoppingToken);

        logger.LogInformation(
            "MenuItemAnalyticsNightlyRecomputeService: repaired {Count} inconsistent rows for {Date}",
            inconsistent.Count, today);
    }

    private static DateTime NextRunAt(DateTime now, int hour)
    {
        var candidate = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Utc);
        return candidate <= now ? candidate.AddDays(1) : candidate;
    }
}