namespace Ordering.Application.Dashboard.Dtos;

public record OpenCheckDto(
    Guid OrderId,
    string OrderNumber,
    string TableLabel,
    decimal? Balance,
    int AgeMinutes);
