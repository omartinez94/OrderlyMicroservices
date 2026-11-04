using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Restaurants.GetFloorPulse;

internal class GetFloorPulseQueryHandler(
    CatalogDbContext dbContext,
    IClock clock)
    : IQueryHandler<GetFloorPulseQuery, GetFloorPulseResult>
{
    public async Task<GetFloorPulseResult> Handle(GetFloorPulseQuery query, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            dbContext.Restaurants.AsNoTracking(),
            r => r.Id == query.RestaurantId,
            cancellationToken);

        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(query.RestaurantId.ToString());
        }

        List<Table> tables = await EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.Tables.AsNoTracking().Where(t => t.RestaurantId == query.RestaurantId),
            cancellationToken);

        int totalTables = tables.Count;
        int occupiedTables = tables.Count(t => t.Status == TableStatus.Occupied);
        int needsCleaningCount = tables.Count(t => t.Status == TableStatus.Cleaning || t.Status == TableStatus.NeedsAttention);
        int reservedUpcomingCount = tables.Count(t => t.Status == TableStatus.Reserved);
        int activeCovers = tables.Where(t => t.Status == TableStatus.Occupied).Sum(t => t.Capacity);
        int totalCapacity = tables.Sum(t => t.Capacity);
        double avgTableTurn = restaurant.EstimatedTurnoverMinutes > 0 ? (double)restaurant.EstimatedTurnoverMinutes : 30.0;

        Instant now = clock.GetCurrentInstant();
        List<WalkInQueue> waitingEntries = await EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.WalkInQueues.AsNoTracking()
                .Where(w => w.RestaurantId == query.RestaurantId && w.Status == WalkInQueueStatus.Waiting)
                .OrderBy(w => w.CreatedAt),
            cancellationToken);

        int waitlistCount = waitingEntries.Count;
        int estimatedWaitMinutes = totalTables > 0
            ? (int)Math.Round((double)waitlistCount * (avgTableTurn / Math.Max(1, totalTables)))
            : waitlistCount * 15;

        List<WaitlistData> topWaitlist = waitingEntries
            .Take(10)
            .Select(w => new WaitlistData(
                new Guid(w.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                w.CustomerName,
                w.PartySize,
                (int)Math.Max(0, (now - w.CreatedAt).TotalMinutes)))
            .ToList();

        FloorPulseData floorPulse = new(
            totalTables,
            occupiedTables,
            needsCleaningCount,
            reservedUpcomingCount,
            activeCovers,
            totalCapacity,
            avgTableTurn,
            waitlistCount,
            estimatedWaitMinutes);

        return new GetFloorPulseResult(floorPulse, topWaitlist, restaurant.Currency);
    }
}
