using Ordering.Application.Common.Interfaces;

namespace Ordering.Application.Orders.Queries.GetOrdersStats;

public class GetOrdersStatsHandler(
    IApplicationDbContext dbContext,
    ICurrentRestaurantProvider restaurantProvider,
    IRestaurantTimeZoneProvider timeZoneProvider,
    IClock clock)
    : IQueryHandler<GetOrdersStatsQuery, GetOrdersStatsResult>
{
    public const int DefaultPrepMinutes = 15;
    public const int PendingLateThresholdSeconds = 120;
    public const int PendingAtRiskThresholdSeconds = 90;
    public const int ConfirmedLateThresholdSeconds = 300;
    public const int ConfirmedAtRiskThresholdSeconds = 225;
    public const int ReadyLateThresholdSeconds = 300;
    public const int ReadyAtRiskThresholdSeconds = 225;
    public const double PreparingAtRiskRatio = 0.75;

    private static readonly OrderStatus[] ActiveStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Preparing,
        OrderStatus.Ready,
        OrderStatus.OnHold,
        OrderStatus.CancelRequested,
    ];

    public async Task<GetOrdersStatsResult> Handle(
        GetOrdersStatsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Instant serverTime = clock.GetCurrentInstant();
        Guid restaurantId = restaurantProvider.RestaurantId;
        RestaurantTimeZoneResolution tz = await timeZoneProvider.GetTimeZoneAsync(
            restaurantId,
            cancellationToken);

        (Instant dayStart, Instant dayEnd) = ComputeTodayBounds(serverTime, tz.Zone);

        List<Order> relevantOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status != OrderStatus.Ordering)
            .Where(o =>
                ActiveStatuses.Contains(o.Status) ||
                (o.CreatedAt.HasValue && o.CreatedAt >= dayStart && o.CreatedAt < dayEnd) ||
                (!o.CreatedAt.HasValue && o.ConfirmedAt.HasValue && o.ConfirmedAt >= dayStart && o.ConfirmedAt < dayEnd))
            .ToListAsync(cancellationToken);

        List<Order> activeOrders = relevantOrders
            .Where(o => ActiveStatuses.Contains(o.Status))
            .ToList();

        List<Order> todayOrders = relevantOrders
            .Where(o => IsOrderInWindow(o, dayStart, dayEnd))
            .ToList();

        (int atRiskCount, int lateCount, Instant? oldestActive) = ComputeActiveSlaMetrics(
            activeOrders,
            serverTime);

        Dictionary<string, int> countsByStatus = BuildCountsByStatus(relevantOrders);
        Dictionary<string, int> countsByType = BuildCountsByType(todayOrders);
        double? avgPrepToday = ComputeAveragePrepMinutes(todayOrders);
        decimal? revenueToday = query.IncludeRevenue
            ? ComputeRevenueToday(todayOrders)
            : null;

        OrdersStatsDto stats = new(
            ServerTime: serverTime,
            TimeZone: tz.TimeZoneId,
            TimeZoneIsFallback: tz.IsFallback,
            CountsByStatus: countsByStatus,
            CountsByType: countsByType,
            ActiveCount: activeOrders.Count,
            LateCount: lateCount,
            AtRiskCount: atRiskCount,
            AvgPrepMinutesToday: avgPrepToday,
            OldestActiveCreatedAt: oldestActive,
            OrdersToday: todayOrders.Count,
            RevenueToday: revenueToday);

        return new GetOrdersStatsResult(stats);
    }

    internal static (Instant DayStart, Instant DayEnd) ComputeTodayBounds(
        Instant serverTime,
        DateTimeZone zone)
    {
        LocalDate localToday = serverTime.InZone(zone).Date;
        Instant start = zone.AtStartOfDay(localToday).ToInstant();
        Instant end = zone.AtStartOfDay(localToday.PlusDays(1)).ToInstant();
        return (start, end);
    }

    private static bool IsOrderInWindow(Order order, Instant start, Instant end)
    {
        Instant? anchor = order.CreatedAt ?? order.ConfirmedAt;
        if (!anchor.HasValue)
        {
            return false;
        }

        return anchor.Value >= start && anchor.Value < end;
    }

    private static (int AtRiskCount, int LateCount, Instant? OldestActive) ComputeActiveSlaMetrics(
        IReadOnlyList<Order> activeOrders,
        Instant serverTime)
    {
        int atRisk = 0;
        int late = 0;
        Instant? oldest = null;

        foreach (Order order in activeOrders)
        {
            Instant? start = order.ConfirmedAt ?? order.CreatedAt;
            if (start.HasValue && (!oldest.HasValue || start.Value < oldest.Value))
            {
                oldest = start.Value;
            }

            Instant phaseAnchor = order.StatusChangedAt ?? order.ConfirmedAt ?? order.CreatedAt ?? serverTime;
            double elapsedSeconds = (serverTime - phaseAnchor).TotalSeconds;

            switch (order.Status)
            {
                case OrderStatus.CancelRequested:
                    late++;
                    break;

                case OrderStatus.Pending:
                    if (elapsedSeconds >= PendingLateThresholdSeconds)
                    {
                        late++;
                    }
                    else if (elapsedSeconds >= PendingAtRiskThresholdSeconds)
                    {
                        atRisk++;
                    }
                    break;

                case OrderStatus.Confirmed:
                    if (elapsedSeconds >= ConfirmedLateThresholdSeconds)
                    {
                        late++;
                    }
                    else if (elapsedSeconds >= ConfirmedAtRiskThresholdSeconds)
                    {
                        atRisk++;
                    }
                    break;

                case OrderStatus.Preparing:
                    int prepMinutes = order.EstimatedPrepTimeMinutes > 0
                        ? order.EstimatedPrepTimeMinutes
                        : DefaultPrepMinutes;
                    double targetSeconds = prepMinutes * 60.0;

                    if (elapsedSeconds >= targetSeconds)
                    {
                        late++;
                    }
                    else if (elapsedSeconds >= targetSeconds * PreparingAtRiskRatio)
                    {
                        atRisk++;
                    }
                    break;

                case OrderStatus.Ready:
                    if (elapsedSeconds >= ReadyLateThresholdSeconds)
                    {
                        late++;
                    }
                    else if (elapsedSeconds >= ReadyAtRiskThresholdSeconds)
                    {
                        atRisk++;
                    }
                    break;

                case OrderStatus.OnHold:
                default:
                    // On hold or unhandled: no SLA penalty
                    break;
            }
        }

        return (atRisk, late, oldest);
    }

    private static Dictionary<string, int> BuildCountsByStatus(IReadOnlyList<Order> orders)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal)
        {
            [nameof(OrderStatus.Pending)] = 0,
            [nameof(OrderStatus.Confirmed)] = 0,
            [nameof(OrderStatus.Preparing)] = 0,
            [nameof(OrderStatus.Ready)] = 0,
            [nameof(OrderStatus.OnHold)] = 0,
            [nameof(OrderStatus.CancelRequested)] = 0,
            [nameof(OrderStatus.Delivered)] = 0,
            [nameof(OrderStatus.Completed)] = 0,
            [nameof(OrderStatus.Cancelled)] = 0,
        };

        foreach (Order order in orders)
        {
            string key = order.Status.ToString();
            if (counts.TryGetValue(key, out int current))
            {
                counts[key] = current + 1;
            }
        }

        return counts;
    }

    private static Dictionary<string, int> BuildCountsByType(IReadOnlyList<Order> todayOrders)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal)
        {
            [nameof(OrderType.DineIn)] = 0,
            [nameof(OrderType.Takeout)] = 0,
            [nameof(OrderType.Delivery)] = 0,
        };

        foreach (Order order in todayOrders)
        {
            string key = order.OrderType.ToString();
            if (counts.TryGetValue(key, out int current))
            {
                counts[key] = current + 1;
            }
        }

        return counts;
    }

    private static double? ComputeAveragePrepMinutes(IReadOnlyList<Order> todayOrders)
    {
        List<double> prepDurations = [];

        foreach (Order order in todayOrders)
        {
            double? duration = TryResolvePrepDurationMinutes(order);
            if (duration.HasValue)
            {
                prepDurations.Add(duration.Value);
            }
        }

        if (prepDurations.Count == 0)
        {
            return null;
        }

        return Math.Round(prepDurations.Average(), 1);
    }

    private static double? TryResolvePrepDurationMinutes(Order order)
    {
        if (order.ActualPrepTimeMinutes > 0)
        {
            return order.ActualPrepTimeMinutes;
        }

        Instant? start = order.ConfirmedAt ?? order.CreatedAt;
        if (order.ReadyAt.HasValue && start.HasValue && order.ReadyAt.Value >= start.Value)
        {
            return (order.ReadyAt.Value - start.Value).TotalMinutes;
        }

        return null;
    }

    private static decimal ComputeRevenueToday(IReadOnlyList<Order> todayOrders) =>
        todayOrders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Sum(o => o.TotalAmount);
}
