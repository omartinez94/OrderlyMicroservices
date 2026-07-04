namespace Kitchen.API.Infrastructure.Repositories;

public class KitchenTicketRepository(KitchenDbContext dbContext) : IKitchenTicketRepository
{
    public async Task<KitchenTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Tickets
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == KitchenTicketId.Of(id), cancellationToken);
    }

    public async Task<IReadOnlyList<KitchenTicket>> GetQueueAsync(
        Guid? restaurantId,
        Guid? stationId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<KitchenTicket> query = dbContext.Tickets
            .Include(t => t.Items)
            .Where(t => t.Status == KitchenTicketStatus.New || t.Status == KitchenTicketStatus.InProgress);

        if (restaurantId.HasValue)
        {
            query = query.Where(t => t.RestaurantId == restaurantId.Value);
        }

        // Station filtering requires joining through items. Currently stations
        // are optional on items; M3 will tighten the assignment discipline.
        if (stationId.HasValue)
        {
            query = query.Where(t => t.Items.Any(i => i.StationId == stationId.Value));
        }

        return await query
            .OrderBy(t => t.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(KitchenTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        await dbContext.Tickets.AddAsync(ticket, cancellationToken);
    }

    public void Update(KitchenTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        dbContext.Tickets.Update(ticket);
    }
}