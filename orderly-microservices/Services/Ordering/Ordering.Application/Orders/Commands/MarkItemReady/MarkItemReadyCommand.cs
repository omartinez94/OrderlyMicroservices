namespace Ordering.Application.Orders.Commands.MarkItemReady;

/// <summary>
/// Marks a single <c>OrderItem</c> as <c>Ready</c>. Permitted only while
/// the item's <see cref="PrepStatus"/> is <c>Preparing</c>; otherwise the
/// aggregate throws and the global handler maps to 409.
/// </summary>
public record MarkItemReadyCommand(Guid OrderId, Guid OrderItemId) : ICommand<MarkItemReadyResult>;

public record MarkItemReadyResult(bool IsSuccess);