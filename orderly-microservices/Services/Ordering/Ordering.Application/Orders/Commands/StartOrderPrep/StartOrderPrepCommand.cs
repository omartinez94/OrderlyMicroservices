namespace Ordering.Application.Orders.Commands.StartOrderPrep;

/// <summary>
/// Drives an order from <c>Confirmed</c> to <c>Preparing</c>. Called by
/// the kitchen UI when prep starts on the order (e.g. the first item
/// gets started). The aggregate's <see cref="Order.MarkPreparing"/>
/// enforces the legal-transition guard.
/// </summary>
public record StartOrderPrepCommand(Guid OrderId) : ICommand<StartOrderPrepResult>;

public record StartOrderPrepResult(bool IsSuccess);