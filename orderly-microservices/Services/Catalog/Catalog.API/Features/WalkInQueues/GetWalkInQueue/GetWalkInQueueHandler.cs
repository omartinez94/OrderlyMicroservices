using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.WalkInQueues.GetWalkInQueue;

public record GetWalkInQueueQuery(Guid RestaurantId, WalkInQueueStatus? Status = null) : IQuery<GetWalkInQueueResult>;

public record GetWalkInQueueResult(IEnumerable<WalkInQueue> Entries);

internal class GetWalkInQueueQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetWalkInQueueQuery, GetWalkInQueueResult>
{
    public async Task<GetWalkInQueueResult> Handle(GetWalkInQueueQuery query, CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.WalkInQueues
            .AsNoTracking()
            .Where(w => w.RestaurantId == query.RestaurantId);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(w => w.Status == query.Status.Value);
        }

        var entries = await EntityFrameworkQueryableExtensions.ToListAsync(
            baseQuery.OrderBy(w => w.CreatedAt),
            cancellationToken);

        return new GetWalkInQueueResult(entries);
    }
}
