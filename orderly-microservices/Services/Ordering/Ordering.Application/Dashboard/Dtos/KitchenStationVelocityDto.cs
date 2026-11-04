namespace Ordering.Application.Dashboard.Dtos;

public record KitchenStationVelocityDto(
    Guid StationId,
    string StationName,
    int ActiveTicketsCount,
    double? AvgPrepMinutes,
    bool IsDelayed);
