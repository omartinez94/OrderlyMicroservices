namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="OrderCancelRequestedIntegrationEvent"/> from Ordering when staff
/// requests cancellation of an order. Flags the kitchen ticket as cancel-requested so
/// KDS staff can see the request and approve or reject it.
/// </summary>
public class OrderCancelRequestedIntegrationEventHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IKitchenTicketBroadcaster broadcaster,
    ILogger<OrderCancelRequestedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<OrderCancelRequestedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelRequestedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OrderCancelRequestedIntegrationEvent message = context.Message;

        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId}",
            nameof(OrderCancelRequestedIntegrationEvent),
            message.OrderId);

        using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
            ? currentRestaurantProvider?.Attach(message.RestaurantId)
            : null;

        KitchenTicket? ticket = await repository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (ticket is null)
        {
            logger.LogWarning(
                "Skipping {Event} — KitchenTicket for Order {OrderId} not found.",
                nameof(OrderCancelRequestedIntegrationEvent),
                message.OrderId);
            return;
        }

        if (ticket.Status is KitchenTicketStatus.Cancelled or KitchenTicketStatus.CancelledPendingAck or KitchenTicketStatus.Bumped)
        {
            logger.LogInformation(
                "Skipping {Event} — KitchenTicket {TicketId} is already in terminal/cancelled status {Status}.",
                nameof(OrderCancelRequestedIntegrationEvent),
                ticket.Id.Value,
                ticket.Status);
            return;
        }

        ticket.MarkCancelRequested(message.RequestedAt, message.Reason);

        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        await broadcaster.BroadcastTicketUpdatedAsync(ticket, context.CancellationToken);

        logger.LogInformation(
            "Marked KitchenTicket {TicketId} as cancel-requested for Order {OrderId} (Reason: {Reason}).",
            ticket.Id.Value,
            message.OrderId,
            message.Reason);
    }
}
