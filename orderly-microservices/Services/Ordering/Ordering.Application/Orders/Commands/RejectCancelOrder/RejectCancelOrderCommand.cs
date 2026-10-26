namespace Ordering.Application.Orders.Commands.RejectCancelOrder;

public record RejectCancelOrderCommand(Guid OrderId, string? Reason = null) : ICommand<RejectCancelOrderResult>;

public record RejectCancelOrderResult(bool IsSuccess);
