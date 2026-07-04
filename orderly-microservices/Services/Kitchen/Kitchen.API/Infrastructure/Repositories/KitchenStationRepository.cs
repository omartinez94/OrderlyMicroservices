namespace Kitchen.API.Infrastructure.Repositories;

public class KitchenStationRepository(KitchenDbContext dbContext) : IKitchenStationRepository
{
    public Task<KitchenStation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Stations
            .FirstOrDefaultAsync(s => s.Id == StationId.Of(id), cancellationToken);
    }

    public async Task<IReadOnlyList<KitchenStation>> ListByRestaurantAsync(
        Guid restaurantId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<KitchenStation> query = dbContext.Stations.Where(s => s.RestaurantId == restaurantId);

        if (activeOnly)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query.OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(KitchenStation station, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        await dbContext.Stations.AddAsync(station, cancellationToken);
    }
}