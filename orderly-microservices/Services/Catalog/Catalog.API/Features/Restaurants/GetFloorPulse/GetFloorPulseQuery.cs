namespace Catalog.API.Features.Restaurants.GetFloorPulse;

public record FloorPulseData(
    int TotalTables,
    int OccupiedTables,
    int NeedsCleaningCount,
    int ReservedUpcomingCount,
    int ActiveCovers,
    int TotalCapacity,
    double? AvgTableTurnMinutes,
    int WaitlistCount,
    int EstimatedWaitMinutes);

public record WaitlistData(
    Guid EntryId,
    string PartyName,
    int PartySize,
    int WaitMinutes);

public record GetFloorPulseQuery(Guid RestaurantId) : IQuery<GetFloorPulseResult>;

public record GetFloorPulseResult(
    FloorPulseData FloorPulse,
    IReadOnlyList<WaitlistData> Waitlist,
    string Currency);
