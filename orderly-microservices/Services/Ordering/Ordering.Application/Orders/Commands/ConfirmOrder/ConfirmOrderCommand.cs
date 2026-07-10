namespace Ordering.Application.Orders.Commands.ConfirmOrder;

/// <summary>
/// Drives an order from <c>Pending</c> to <c>Confirmed</c>. Called by the
/// kitchen UI / REST agent after the cashier hands the ticket to the
/// kitchen. The aggregate's <see cref="Order.Confirm"/> enforces the
/// legal-transition guard.
/// </summary>
public record ConfirmOrderCommand(Guid OrderId) : ICommand<ConfirmOrderResult>;

public record ConfirmOrderResult(bool IsSuccess);