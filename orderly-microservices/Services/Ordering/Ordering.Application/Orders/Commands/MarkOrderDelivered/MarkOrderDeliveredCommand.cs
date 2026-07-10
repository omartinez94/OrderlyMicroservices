namespace Ordering.Application.Orders.Commands.MarkOrderDelivered;

/// <summary>
/// Drives an order from <c>Ready</c> to <c>Delivered</c>. Called by the
/// cashier / courier when the order is handed to the customer. The
/// aggregate's <see cref="Order.MarkDelivered"/> enforces the
/// legal-transition guard.
/// </summary>
public record MarkOrderDeliveredCommand(Guid OrderId) : ICommand<MarkOrderDeliveredResult>;

public record MarkOrderDeliveredResult(bool IsSuccess);