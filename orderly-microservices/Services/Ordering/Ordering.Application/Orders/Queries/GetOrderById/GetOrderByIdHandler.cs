using Ordering.Domain.Exceptions;

namespace Ordering.Application.Orders.Queries.GetOrderById;

public class GetOrderByIdHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrderByIdQuery, GetOrderByIdResult>
{
    public async Task<GetOrderByIdResult> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        Order? order;

        if (Guid.TryParse(query.Identifier, out Guid guidId))
        {
            order = await dbContext.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Activities)
                .AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == OrderId.Of(guidId), cancellationToken);
        }
        else
        {
            OrderNumber? orderNumber = null;
            try
            {
                orderNumber = OrderNumber.Of(query.Identifier);
            }
            catch (DomainException)
            {
                // Not a valid OrderNumber format
            }

            IQueryable<Order> baseQuery = dbContext.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Activities)
                .AsNoTracking();

            if (orderNumber is not null)
            {
                order = await baseQuery
                    .SingleOrDefaultAsync(o => o.ShortId == query.Identifier || o.OrderNumber == orderNumber, cancellationToken);
            }
            else
            {
                order = await baseQuery
                    .SingleOrDefaultAsync(o => o.ShortId == query.Identifier, cancellationToken);
            }
        }

        if (order is null)
        {
            throw new OrderNotFoundException(nameof(Order), query.Identifier);
        }

        return new GetOrderByIdResult(order.ToOrderDto());
    }
}
