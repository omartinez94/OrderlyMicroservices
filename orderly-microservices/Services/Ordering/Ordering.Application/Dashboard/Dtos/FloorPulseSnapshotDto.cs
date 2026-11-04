namespace Ordering.Application.Dashboard.Dtos;

public record FloorPulseSnapshotDto(
    int TotalTables,
    int OccupiedTables,
    int NeedsCleaningCount,
    int ReservedUpcomingCount,
    int ActiveCovers,
    int TotalCapacity,
    double? AvgTableTurnMinutes,
    int WaitlistCount,
    int EstimatedWaitMinutes);
