namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="OrderCancelledIntegrationEvent"/> from Ordering when an order is cancelled
/// externally (by waiter/cashier on Pending, or approved by manager). Transitions the ticket
/// to <c>CancelledPendingAck</c> so it remains visible on the KDS until kitchen staff bump it off.
/// Returns immediately if already Cancelled or CancelledPendingAck to prevent self-ack loop.
/// </summary>
public class OrderCancelledIntegrationEventHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IKitchenTicketBroadcaster broadcaster,
    ILogger<OrderCancelledIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<OrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelledIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OrderCancelledIntegrationEvent message = context.Message;

        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId}",
            nameof(OrderCancelledIntegrationEvent),
            message.OrderId);

        using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
            ? currentRestaurantProvider?.Attach(message.RestaurantId)
            : null;

        KitchenTicket? ticket = await repository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (ticket is null)
        {
            logger.LogWarning(
                "Skipping {Event} — KitchenTicket for Order {OrderId} not found.",
                nameof(OrderCancelledIntegrationEvent),
                message.OrderId);
            return;
        }

        // Self-acknowledgement prevention: if already Cancelled (e.g. cancelled by Kitchen)
        // or CancelledPendingAck, return immediately.
        if (ticket.Status is KitchenTicketStatus.Cancelled or KitchenTicketStatus.CancelledPendingAck)
        {
            logger.LogInformation(
                "Skipping {Event} — KitchenTicket {TicketId} is already in status {Status}.",
                nameof(OrderCancelledIntegrationEvent),
                ticket.Id.Value,
                ticket.Status);
            return;
        }

        ticket.MarkCancelledPendingAck(message.CancelledAt, message.Reason);

        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        await broadcaster.BroadcastTicketUpdatedAsync(ticket, context.CancellationToken);

        logger.LogInformation(
            "Moved KitchenTicket {TicketId} to CancelledPendingAck for Order {OrderId} (Reason: {Reason}).",
            ticket.Id.Value,
            message.OrderId,
            message.Reason);
    }
}
