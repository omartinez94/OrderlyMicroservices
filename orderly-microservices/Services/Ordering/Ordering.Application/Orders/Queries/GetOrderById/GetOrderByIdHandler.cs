namespace Ordering.Application.Orders.Queries.GetOrderById;

public class GetOrderByIdHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrderByIdQuery, GetOrderByIdResult>
{
    public async Task<GetOrderByIdResult> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == OrderId.Of(request.Id), cancellationToken) ?? throw new OrderNotFoundException(nameof(Order), request.Id);

        return new GetOrderByIdResult(order.ToOrderDto());
    }
}
