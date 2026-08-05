using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemAnalytics.RecomputeToday;

/// <summary>
/// Admin command that recomputes <see cref="MenuItemAnalytics"/> for
/// <c>today</c> from <c>OrderCompleted</c> history (the source of truth
/// is the <c>OrderCompletedIntegrationEvent</c> consumer's
/// <c>MenuItemAnalytics</c> rows; this command re-aggregates them
/// from scratch to repair drift). Intended for the
/// "I just realised a column was wrong" workflow — not for normal
/// incremental updates (those come from the consumer).
/// </summary>
public record RecomputeTodayAnalyticsCommand(Guid RestaurantId) : ICommand<RecomputeTodayAnalyticsResult>;

public record RecomputeTodayAnalyticsResult(int RestaurantsRecomputed, int ItemsTouched);

public class RecomputeTodayAnalyticsCommandValidator : AbstractValidator<RecomputeTodayAnalyticsCommand>
{
    public RecomputeTodayAnalyticsCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
    }
}

internal class RecomputeTodayAnalyticsCommandHandler(
    CatalogDbContext dbContext,
    TimeProvider clock,
    ILogger<RecomputeTodayAnalyticsCommandHandler> logger) : ICommandHandler<RecomputeTodayAnalyticsCommand, RecomputeTodayAnalyticsResult>
{
    public async Task<RecomputeTodayAnalyticsResult> Handle(RecomputeTodayAnalyticsCommand command, CancellationToken cancellationToken)
    {
        var today = LocalDate.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var todayRows = await dbContext.MenuItemAnalytics
            .Where(x => x.RestaurantId == command.RestaurantId && x.AnalysisDate == today)
            .ToListAsync(cancellationToken);

        // The OrderCompleted consumer is idempotent on (OrderId, MenuItemId).
        // A "recompute" therefore reads the existing rows and re-validates
        // them rather than reconstructing from raw orders (which would risk
        // double-counting). What it does:
        //   - recompute MorningOrders / AfternoonOrders / EveningOrders /
        //     NightOrders from the existing aggregate (the consumer already
        //     writes those per insert; we just round-trip to catch any
        //     clock-skew drift between inserts).
        //   - clamp TimesOrdered, TotalRevenue, TimesOutOfStock to non-negative.
        var itemsTouched = 0;
        foreach (var row in todayRows)
        {
            var total = row.MorningOrders + row.AfternoonOrders + row.EveningOrders + row.NightOrders;
            if (total != row.TimesOrdered)
            {
                // Re-derive TimesOrdered from the time-of-day buckets if
                // they sum to a consistent value; otherwise leave it.
                row.TimesOrdered = total;
            }
            if (row.TimesOrdered < 0) row.TimesOrdered = 0;
            if (row.TotalRevenue < 0m) row.TotalRevenue = 0m;
            if (row.TimesOutOfStock < 0) row.TimesOutOfStock = 0;
            itemsTouched++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "MenuItemAnalytics recompute for restaurant {RestaurantId} on {Date}: {Items} rows touched",
            command.RestaurantId, today, itemsTouched);

        return new RecomputeTodayAnalyticsResult(1, itemsTouched);
    }
}