namespace Ordering.Application.Orders.Queries.GetOrderById;

public record GetOrderByIdQuery(Guid Id) : IQuery<GetOrderByIdResult>;

public record GetOrderByIdResult(OrderDto Order);
