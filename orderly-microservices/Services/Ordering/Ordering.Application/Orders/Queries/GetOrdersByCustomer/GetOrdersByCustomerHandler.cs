namespace Ordering.Application.Orders.Queries.GetOrdersByCustomer;

public class GetOrdersByCustomerHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrdersByCustomerQuery, GetOrdersByCustomerResult>
{
    public async Task<GetOrdersByCustomerResult> Handle(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        // OrderBy over the OrderNumber value object (not .Value) — the
        // EF Core value converter translates the OrderNumber column to
        // its underlying string at the SQL boundary, so the LINQ
        // expression must stay on the value-object surface. Sorted
        // lexical on the underlying string matches the in-memory
        // OrderNumber.CompareTo contract (used by the unit-test mock).
        List<Order>? orders = await dbContext.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Activities)
            .AsNoTracking()
            .Where(o => o.CustomerId == CustomerId.Of(query.CustomerId))
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(cancellationToken);

        return new GetOrdersByCustomerResult(orders.ToOrderDtoList());
    }
}
