namespace Ordering.Infrastructure.Services;

using BuildingBlocks.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dashboard.Dtos;
using Ordering.Application.Data;
using Ordering.Domain.Models;
using Ordering.Infrastructure.Caching;

public class DashboardSummaryService(
    IApplicationDbContext dbContext,
    IRestaurantTimeZoneProvider timeZoneProvider,
    ICatalogDashboardReader catalogReader,
    IKitchenDashboardReader kitchenReader,
    IDashboardSnapshotCache cache,
    IClock clock,
    ILogger<DashboardSummaryService> logger)
    : IDashboardSummaryService
{
    private const double DefaultTargetPrepMinutes = 15.0;

    private static readonly OrderStatus[] ActiveStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Preparing,
        OrderStatus.Ready,
        OrderStatus.OnHold,
        OrderStatus.CancelRequested,
    ];

    public async Task<RestaurantOperationsDashboardDto> GetLiveSummaryAsync(
        Guid restaurantId,
        bool includeRevenue,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrComputeAsync(
            restaurantId,
            includeRevenue,
            () => ComputeRawSnapshotAsync(restaurantId, cancellationToken),
            cancellationToken);
    }

    private async Task<RestaurantOperationsDashboardDto> ComputeRawSnapshotAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        Instant serverTime = clock.GetCurrentInstant();

        // 1. Resolve TimeZone
        RestaurantTimeZoneResolution tz = await timeZoneProvider.GetTimeZoneAsync(restaurantId, cancellationToken);
        DateTimeZone zone = tz.Zone;
        LocalDate localToday = serverTime.InZone(zone).Date;
        Instant dayStart = zone.AtStartOfDay(localToday).ToInstant();
        Instant dayEnd = zone.AtStartOfDay(localToday.PlusDays(1)).ToInstant();

        // Historical 7-day bounds
        Instant historyStart = zone.AtStartOfDay(localToday.PlusDays(-7)).ToInstant();

        // 2. Fetch Orders for Today and Active
        List<Order> todayAndActiveOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && o.Status != OrderStatus.Ordering)
            .Where(o => ActiveStatuses.Contains(o.Status) || (o.CreatedAt >= dayStart && o.CreatedAt < dayEnd))
            .ToListAsync(cancellationToken);

        // Fetch Historical 7-day orders for baseline
        List<Order> historicalOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && o.Status != OrderStatus.Ordering && o.Status != OrderStatus.Cancelled)
            .Where(o => o.CreatedAt >= historyStart && o.CreatedAt < dayStart)
            .ToListAsync(cancellationToken);

        // 3. Downstream Calls (Catalog & Kitchen)
        Task<CatalogFloorPulseResult> catalogTask = catalogReader.GetFloorPulseAsync(restaurantId, cancellationToken);
        Task<KitchenVelocityResult> kitchenTask = kitchenReader.GetVelocityAsync(restaurantId, cancellationToken);
        await Task.WhenAll(catalogTask, kitchenTask);

        CatalogFloorPulseResult catalogResult = await catalogTask;
        KitchenVelocityResult kitchenResult = await kitchenTask;

        string currency = catalogResult.Currency ?? "USD";

        // 4. Compute Ordering Aggregates
        List<Order> activeOrders = todayAndActiveOrders
            .Where(o => ActiveStatuses.Contains(o.Status))
            .ToList();

        List<Order> todayOrders = todayAndActiveOrders
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd)
            .ToList();

        List<Order> todayNonCancelled = todayOrders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .ToList();

        decimal netSalesToday = todayNonCancelled.Sum(o => o.TotalAmount);
        int activeOrdersCount = activeOrders.Count;

        // SLA counts
        int lateOrdersCount = 0;
        int atRiskOrdersCount = 0;
        foreach (Order order in activeOrders)
        {
            Instant phaseAnchor = order.StatusChangedAt ?? order.ConfirmedAt ?? order.CreatedAt ?? serverTime;
            double elapsedSeconds = (serverTime - phaseAnchor).TotalSeconds;

            if (order.Status == OrderStatus.Preparing)
            {
                double targetSeconds = (order.EstimatedPrepTimeMinutes > 0 ? order.EstimatedPrepTimeMinutes : DefaultTargetPrepMinutes) * 60.0;
                if (elapsedSeconds >= targetSeconds)
                {
                    lateOrdersCount++;
                }
                else if (elapsedSeconds >= targetSeconds * 0.8)
                {
                    atRiskOrdersCount++;
                }
            }
            else if (order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Ready or OrderStatus.CancelRequested)
            {
                if (elapsedSeconds >= 300)
                {
                    lateOrdersCount++;
                }
                else if (elapsedSeconds >= 225)
                {
                    atRiskOrdersCount++;
                }
            }
        }

        // Prep duration stats from completed/ready orders
        List<double> todayPrepMinutes = [];
        List<double> rolling60MinPrep = [];
        Instant rolling60Cutoff = serverTime.Minus(Duration.FromMinutes(60));

        foreach (Order order in todayOrders)
        {
            if (order.ReadyAt.HasValue)
            {
                Instant start = order.ConfirmedAt ?? order.CreatedAt ?? order.ReadyAt.Value;
                if (order.ReadyAt.Value >= start)
                {
                    double prep = (order.ReadyAt.Value - start).TotalMinutes;
                    todayPrepMinutes.Add(prep);
                    if (order.ReadyAt.Value >= rolling60Cutoff)
                    {
                        rolling60MinPrep.Add(prep);
                    }
                }
            }
        }

        double? avgPrepMinutesToday = todayPrepMinutes.Count > 0
            ? Math.Round(todayPrepMinutes.Average(), 1)
            : (kitchenResult.KitchenVelocity?.AvgPrepMinutes);

        double? p90PrepMinutesRolling = null;
        if (rolling60MinPrep.Count > 0)
        {
            rolling60MinPrep.Sort();
            int index = (int)Math.Ceiling(0.9 * rolling60MinPrep.Count) - 1;
            index = Math.Clamp(index, 0, rolling60MinPrep.Count - 1);
            p90PrepMinutesRolling = Math.Round(rolling60MinPrep[index], 1);
        }
        else if (kitchenResult.KitchenVelocity?.P90PrepMinutes != null)
        {
            p90PrepMinutesRolling = kitchenResult.KitchenVelocity.P90PrepMinutes;
        }

        // 7-day growth comparison
        // Compare same elapsed time in the day across past 7 days
        double elapsedHoursToday = (serverTime - dayStart).TotalHours;
        decimal past7DayTotalNetSalesSameElapsed = 0m;
        for (int d = 1; d <= 7; d++)
        {
            Instant pastDayStart = zone.AtStartOfDay(localToday.PlusDays(-d)).ToInstant();
            Instant pastDayElapsedCutoff = pastDayStart.Plus(Duration.FromHours(elapsedHoursToday));
            past7DayTotalNetSalesSameElapsed += historicalOrders
                .Where(o => o.CreatedAt >= pastDayStart && o.CreatedAt < pastDayElapsedCutoff)
                .Sum(o => o.TotalAmount);
        }

        decimal avgPastNetSales = past7DayTotalNetSalesSameElapsed / 7.0m;
        double? salesGrowthPercentage = avgPastNetSales > 0m
            ? (double)Math.Round(((netSalesToday - avgPastNetSales) / avgPastNetSales) * 100m, 1)
            : 0.0;

        // Table counts from Catalog floor-pulse
        int occupiedTables = catalogResult.FloorPulse?.OccupiedTables ?? 0;
        int totalTables = catalogResult.FloorPulse?.TotalTables ?? 0;
        double occupancyPercentage = totalTables > 0
            ? Math.Round(((double)occupiedTables / totalTables) * 100.0, 1)
            : 0.0;

        LiveOperationalTickerDto ticker = new(
            NetSalesToday: netSalesToday,
            SalesTargetToday: null,
            SalesGrowthPercentage: salesGrowthPercentage,
            ActiveOrdersCount: activeOrdersCount,
            LateOrdersCount: lateOrdersCount,
            AtRiskOrdersCount: atRiskOrdersCount,
            OccupiedTablesCount: occupiedTables,
            TotalTablesCount: totalTables,
            OccupancyPercentage: occupancyPercentage,
            AvgPrepMinutesToday: avgPrepMinutesToday,
            P90PrepMinutesRolling: p90PrepMinutesRolling);

        // Hourly Sales Pace (24 hours)
        List<HourlySalesBucketDto> hourlyBuckets = [];
        for (int h = 0; h < 24; h++)
        {
            ZonedDateTime localHourZoned = zone.AtStartOfDay(localToday).Plus(Duration.FromHours(h));
            OffsetDateTime hourOffset = localHourZoned.ToOffsetDateTime();
            Instant hourStartInstant = localHourZoned.ToInstant();
            Instant hourEndInstant = hourStartInstant.Plus(Duration.FromHours(1));

            List<Order> ordersInHour = todayNonCancelled
                .Where(o => o.CreatedAt >= hourStartInstant && o.CreatedAt < hourEndInstant)
                .ToList();

            decimal grossSales = ordersInHour.Sum(o => o.TotalAmount);
            int count = ordersInHour.Count;

            // Historical average for this hour across 7 days
            decimal historicalTotal = 0m;
            for (int d = 1; d <= 7; d++)
            {
                Instant hStart = zone.AtStartOfDay(localToday.PlusDays(-d)).ToInstant().Plus(Duration.FromHours(h));
                Instant hEnd = hStart.Plus(Duration.FromHours(1));
                historicalTotal += historicalOrders
                    .Where(o => o.CreatedAt >= hStart && o.CreatedAt < hEnd)
                    .Sum(o => o.TotalAmount);
            }
            decimal historicalAvg = Math.Round(historicalTotal / 7.0m, 2);

            hourlyBuckets.Add(new HourlySalesBucketDto(
                hourOffset,
                h,
                grossSales,
                count,
                historicalAvg));
        }

        decimal openBalanceTotal = activeOrders.Sum(o => o.TotalAmount);
        decimal cashTenderedTotal = todayNonCancelled
            .Where(o => o.Payment.Method == PaymentMethod.Cash)
            .Sum(o => o.TotalAmount);
        decimal digitalTenderedTotal = todayNonCancelled
            .Where(o => o.Payment.Method is PaymentMethod.Card or PaymentMethod.Wallet)
            .Sum(o => o.TotalAmount);

        SalesPaceDto salesPace = new(
            hourlyBuckets,
            openBalanceTotal,
            DiscountsTotal: 0m,
            DiscountPercentageOfSales: 0.0,
            cashTenderedTotal,
            digitalTenderedTotal);

        // Open Checks (top 10 oldest active orders)
        List<OpenCheckDto> openChecks = activeOrders
            .OrderBy(o => o.CreatedAt ?? serverTime)
            .Take(10)
            .Select(o =>
            {
                int age = (int)Math.Max(0, (serverTime - (o.CreatedAt ?? serverTime)).TotalMinutes);
                string tableLabel = o.TableId.HasValue
                    ? $"T{o.TableId.Value.ToString()[..4].ToUpperInvariant()}"
                    : "Bar";

                return new OpenCheckDto(
                    o.Id.Value,
                    o.OrderNumber.Value,
                    tableLabel,
                    o.TotalAmount,
                    age);
            })
            .ToList();

        // Active Alerts
        List<OperationalAlertDto> alerts = [];

        // Alert: late tickets
        foreach (Order o in activeOrders.Where(x => x.Status == OrderStatus.Preparing))
        {
            Instant start = o.StatusChangedAt ?? o.ConfirmedAt ?? o.CreatedAt ?? serverTime;
            int elapsed = (int)(serverTime - start).TotalMinutes;
            int target = o.EstimatedPrepTimeMinutes > 0 ? o.EstimatedPrepTimeMinutes : (int)DefaultTargetPrepMinutes;
            if (elapsed > target)
            {
                alerts.Add(new OperationalAlertDto(
                    Id: $"kitchen_late:{o.Id.Value}",
                    Timestamp: serverTime,
                    Severity: "critical",
                    Category: "kitchen_late",
                    Title: $"Order #{o.OrderNumber.Value} Delayed",
                    Message: $"Order #{o.OrderNumber.Value} has been in preparation for {elapsed} minutes (Target: {target}m)",
                    ActionUrl: $"/site/restaurant/orders/{o.Id.Value}",
                    ActionLabel: "Expedite Order",
                    EntityId: o.Id.Value.ToString()));
            }
        }

        // Alert: pass dwell
        foreach (Order o in activeOrders.Where(x => x.Status == OrderStatus.Ready))
        {
            Instant readyAt = o.ReadyAt ?? o.StatusChangedAt ?? serverTime;
            int dwellMinutes = (int)(serverTime - readyAt).TotalMinutes;
            if (dwellMinutes >= 5)
            {
                alerts.Add(new OperationalAlertDto(
                    Id: $"pass_dwell:{o.Id.Value}",
                    Timestamp: serverTime,
                    Severity: "warning",
                    Category: "pass_dwell",
                    Title: $"Order #{o.OrderNumber.Value} at the Pass",
                    Message: $"Order #{o.OrderNumber.Value} has been waiting at the pass for {dwellMinutes} minutes",
                    ActionUrl: $"/site/restaurant/orders/{o.Id.Value}",
                    ActionLabel: "Deliver Order",
                    EntityId: o.Id.Value.ToString()));
            }
        }

        // Alert: degraded services
        if (catalogResult.SectionStatus != "ok")
        {
            alerts.Add(new OperationalAlertDto(
                Id: "system_degraded:catalog",
                Timestamp: serverTime,
                Severity: "warning",
                Category: "system_degraded",
                Title: "Catalog Service Degraded",
                Message: "Floor pulse and waitlist telemetry is currently unavailable.",
                ActionUrl: "/site/restaurant",
                ActionLabel: "View Details",
                EntityId: "catalog"));
        }

        if (kitchenResult.SectionStatus != "ok")
        {
            alerts.Add(new OperationalAlertDto(
                Id: "system_degraded:kitchen",
                Timestamp: serverTime,
                Severity: "warning",
                Category: "system_degraded",
                Title: "Kitchen Service Degraded",
                Message: "Live kitchen velocity metrics are operating in degraded mode.",
                ActionUrl: "/site/kitchen",
                ActionLabel: "View Details",
                EntityId: "kitchen"));
        }

        // Meta sections
        Dictionary<string, string> sectionStatuses = new()
        {
            ["ticker"] = "ok",
            ["salesPace"] = "ok",
            ["kitchenVelocity"] = kitchenResult.SectionStatus,
            ["floorPulse"] = catalogResult.SectionStatus,
            ["openChecks"] = "ok",
            ["waitlist"] = catalogResult.SectionStatus,
            ["alerts"] = "ok",
        };

        DashboardMetaDto meta = new(sectionStatuses, serverTime);

        return new RestaurantOperationsDashboardDto(
            ServerTime: serverTime,
            RestaurantId: restaurantId,
            Currency: currency,
            TimeZone: tz.TimeZoneId,
            BusinessDate: localToday,
            Meta: meta,
            Ticker: ticker,
            SalesPace: salesPace,
            KitchenVelocity: kitchenResult.KitchenVelocity,
            FloorPulse: catalogResult.FloorPulse,
            OpenChecks: openChecks,
            Waitlist: catalogResult.Waitlist,
            ActiveAlerts: alerts);
    }
}
