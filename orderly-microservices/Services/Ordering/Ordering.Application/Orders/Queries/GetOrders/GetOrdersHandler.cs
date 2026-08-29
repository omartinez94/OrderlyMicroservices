namespace Ordering.Application.Orders.Queries.GetOrders;

public class GetOrdersHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrdersQuery, GetOrdersResult>
{
    public async Task<GetOrdersResult> Handle(GetOrdersQuery query, CancellationToken cancellationToken)
    {
        int pageIndex = query.PaginationRequest.PageIndex;
        int pageSize = query.PaginationRequest.PageSize;

        long totalCount = await dbContext.Orders.LongCountAsync(cancellationToken);

        List<Order>? orders = await dbContext.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Activities)
            .AsNoTracking()
            .OrderBy(o => o.OrderNumber)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GetOrdersResult(
            new PaginatedResult<OrderDto>(
                pageIndex,
                pageSize,
                totalCount,
                orders.ToOrderDtoList()));
    }
}
