namespace Ordering.Application.Dashboard.Dtos;

public record LiveOperationalTickerDto(
    decimal? NetSalesToday,
    decimal? SalesTargetToday,
    double? SalesGrowthPercentage,
    int ActiveOrdersCount,
    int LateOrdersCount,
    int AtRiskOrdersCount,
    int OccupiedTablesCount,
    int TotalTablesCount,
    double OccupancyPercentage,
    double? AvgPrepMinutesToday,
    double? P90PrepMinutesRolling);
