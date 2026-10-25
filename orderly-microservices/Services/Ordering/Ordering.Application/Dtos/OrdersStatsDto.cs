namespace Ordering.Application.Dtos;

public record OrdersStatsDto(
    Instant ServerTime,
    string TimeZone,
    bool TimeZoneIsFallback,
    IReadOnlyDictionary<string, int> CountsByStatus,
    IReadOnlyDictionary<string, int> CountsByType,
    int ActiveCount,
    int LateCount,
    int AtRiskCount,
    double? AvgPrepMinutesToday,
    Instant? OldestActiveCreatedAt,
    int OrdersToday,
    decimal? RevenueToday);
