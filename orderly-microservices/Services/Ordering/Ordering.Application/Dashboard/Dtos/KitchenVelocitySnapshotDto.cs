namespace Ordering.Application.Dashboard.Dtos;

public record KitchenVelocitySnapshotDto(
    int ActiveTickets,
    double? AvgPrepMinutes,
    double TargetPrepMinutes,
    double? P90PrepMinutes,
    IReadOnlyList<KitchenStationVelocityDto> Stations,
    int PassDwellCount);
