namespace Ordering.Application.Orders.Commands.CreateOrder;

public record CreateOrderCommand(OrderDto Order, Guid? IdempotencyKey = null) : ICommand<CreateOrderResult>;

public record CreateOrderResult(Guid Id, OrderDto? Order = null, bool IsReplay = false);
