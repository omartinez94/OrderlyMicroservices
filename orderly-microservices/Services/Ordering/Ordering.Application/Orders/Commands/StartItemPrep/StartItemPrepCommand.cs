namespace Ordering.Application.Orders.Commands.StartItemPrep;

/// <summary>
/// Marks a single <c>OrderItem</c> as <c>Preparing</c>. Permitted only
/// while the item's <see cref="PrepStatus"/> is <c>Pending</c>; otherwise
/// the aggregate throws <see cref="Ordering.Domain.Exceptions.InvalidOrderItemStateTransitionException"/>
/// which the global handler maps to 409.
/// </summary>
public record StartItemPrepCommand(Guid OrderId, Guid OrderItemId) : ICommand<StartItemPrepResult>;

public record StartItemPrepResult(bool IsSuccess);