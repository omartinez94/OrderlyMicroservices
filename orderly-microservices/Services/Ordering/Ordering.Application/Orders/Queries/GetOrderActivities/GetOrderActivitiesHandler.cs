namespace Ordering.Application.Orders.Queries.GetOrderActivities;

/// <summary>
/// Reads the activity feed for one order, applies the
/// <see cref="GetOrderActivitiesQuery"/> filters, paginates with
/// <see cref="BuildingBlocks.Pagination.PaginationRequest"/>, and returns
/// a <see cref="BuildingBlocks.Pagination.PaginatedResult{T}"/> of
/// <see cref="OrderActivityDto"/> ordered by <c>OccurredAt ASC, Id ASC</c>.
/// </summary>
/// <remarks>
/// <para>
/// Activities are loaded via the <c>Order.Activities</c> navigation in a
/// single query (no <c>DbSet&lt;OrderActivity&gt;</c> exists on
/// <see cref="Data.IApplicationDbContext"/>; see ORDER_ACTIVITY_PLAN.md
/// §0.3 — <c>OrderActivity</c> is a child entity, not an aggregate root).
/// </para>
/// <para>
/// The order is loaded <c>AsNoTracking</c> — the read path does not mutate
/// the aggregate. <c>SingleOrDefaultAsync</c> + a <c>?? throw</c> on miss
/// surfaces <see cref="Ordering.Application.Exceptions.OrderNotFoundException"/>,
/// which the global exception handler maps to 404.
/// </para>
/// </remarks>
public class GetOrderActivitiesHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrderActivitiesQuery, GetOrderActivitiesResult>
{
    public async Task<GetOrderActivitiesResult> Handle(
        GetOrderActivitiesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await dbContext.Orders
            .Include(o => o.Activities)
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == OrderId.Of(query.OrderId), cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), query.OrderId);

        // The filter chain is deliberately a per-call fluent pipeline so
        // every LINQ provider (EF Core here, in-memory in tests) sees the
        // same expression shape. The pagination Skip/Take is applied
        // AFTER the filter; totalCount is captured BEFORE Skip/Take so the
        // PaginatedResult.TotalCount reflects all matching rows, not just
        // the page slice.
        var filtered = order.Activities.AsEnumerable()
            .Where(a => query.Type is null || a.ActivityType == query.Type)
            .Where(a => query.From is null || a.OccurredAt >= query.From.Value)
            .Where(a => query.To is null || a.OccurredAt <= query.To.Value)
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id.Value);

        var totalCount = filtered.Count();

        var pageIndex = query.Pagination.PageIndex;
        var pageSize = query.Pagination.PageSize;

        var data = filtered
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(a => new OrderActivityDto(
                Id: a.Id.Value,
                ActivityType: a.ActivityType,
                ActorUserId: a.ActorUserId,
                OccurredAt: a.OccurredAt,
                CorrelationId: a.CorrelationId,
                Notes: a.Notes,
                Metadata: a.Metadata))
            .ToList();

        return new GetOrderActivitiesResult(
            new PaginatedResult<OrderActivityDto>(pageIndex, pageSize, totalCount, data));
    }
}
