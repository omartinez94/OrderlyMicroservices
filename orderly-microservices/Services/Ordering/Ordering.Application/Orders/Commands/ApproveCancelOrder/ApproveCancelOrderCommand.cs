namespace Ordering.Application.Orders.Commands.ApproveCancelOrder;

public record ApproveCancelOrderCommand(Guid OrderId, string? Reason = null) : ICommand<ApproveCancelOrderResult>;

public record ApproveCancelOrderResult(bool IsSuccess);
