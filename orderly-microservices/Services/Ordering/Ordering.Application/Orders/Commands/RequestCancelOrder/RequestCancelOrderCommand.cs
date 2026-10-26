namespace Ordering.Application.Orders.Commands.RequestCancelOrder;

public record RequestCancelOrderCommand(Guid OrderId, string Reason) : ICommand<RequestCancelOrderResult>;

public record RequestCancelOrderResult(bool IsSuccess);
