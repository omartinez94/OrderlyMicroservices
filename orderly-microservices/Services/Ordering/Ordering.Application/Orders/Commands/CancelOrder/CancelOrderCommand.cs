namespace Ordering.Application.Orders.Commands.CancelOrder;

/// <summary>
/// Cancels an order. Body shape: <c>{ "reason": "..." }</c>. Permitted
/// from any non-terminal state (Pending, Confirmed, Preparing, Ready);
/// throws <see cref="Ordering.Domain.Exceptions.InvalidOrderStateTransitionException"/>
/// when the order is already in a terminal state.
/// </summary>
public record CancelOrderCommand(Guid OrderId, string Reason) : ICommand<CancelOrderResult>;

public record CancelOrderResult(bool IsSuccess);