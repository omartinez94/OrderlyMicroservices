namespace Ordering.Application.Orders.Commands.MarkOrderReady;

/// <summary>
/// Drives an order from <c>Preparing</c> to <c>Ready</c>. The kitchen UI
/// fires this when the order is ready for pickup / dispatch.
/// </summary>
public record MarkOrderReadyCommand(Guid OrderId) : ICommand<MarkOrderReadyResult>;

public record MarkOrderReadyResult(bool IsSuccess);