namespace Ordering.Application.Orders.Queries.GetOrderById;

public class GetOrderByIdHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrderByIdQuery, GetOrderByIdResult>
{
    public async Task<GetOrderByIdResult> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Activities)
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == OrderId.Of(query.Id), cancellationToken) ?? throw new OrderNotFoundException(nameof(Order), query.Id);

        return new GetOrderByIdResult(order.ToOrderDto());
    }
}
