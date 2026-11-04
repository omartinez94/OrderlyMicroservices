namespace Ordering.Application.Dashboard.Dtos;

public record SalesPaceDto(
    IReadOnlyList<HourlySalesBucketDto> HourlySales,
    decimal? OpenBalanceTotal,
    decimal? DiscountsTotal,
    double? DiscountPercentageOfSales,
    decimal? CashTenderedTotal,
    decimal? DigitalTenderedTotal);
