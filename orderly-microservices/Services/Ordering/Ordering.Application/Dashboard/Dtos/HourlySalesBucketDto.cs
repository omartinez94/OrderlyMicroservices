namespace Ordering.Application.Dashboard.Dtos;

public record HourlySalesBucketDto(
    OffsetDateTime HourStart,
    int HourOfDay,
    decimal? GrossSales,
    int OrderCount,
    decimal? HistoricalAverageSales);
