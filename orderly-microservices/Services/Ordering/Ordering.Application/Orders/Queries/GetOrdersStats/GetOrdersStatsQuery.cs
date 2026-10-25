namespace Ordering.Application.Orders.Queries.GetOrdersStats;

public record GetOrdersStatsQuery(bool IncludeRevenue = false) : IQuery<GetOrdersStatsResult>;

public record GetOrdersStatsResult(OrdersStatsDto Stats);
