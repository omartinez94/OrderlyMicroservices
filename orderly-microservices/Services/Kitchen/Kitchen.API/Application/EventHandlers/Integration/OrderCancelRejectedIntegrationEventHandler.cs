namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="OrderCancelRejectedIntegrationEvent"/> from Ordering when manager/admin
/// rejects an order cancellation request. Clears the cancel request flag on the KDS ticket.
/// </summary>
public class OrderCancelRejectedIntegrationEventHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IKitchenTicketBroadcaster broadcaster,
    ILogger<OrderCancelRejectedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<OrderCancelRejectedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelRejectedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OrderCancelRejectedIntegrationEvent message = context.Message;

        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId}",
            nameof(OrderCancelRejectedIntegrationEvent),
            message.OrderId);

        using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
            ? currentRestaurantProvider?.Attach(message.RestaurantId)
            : null;

        KitchenTicket? ticket = await repository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (ticket is null)
        {
            logger.LogWarning(
                "Skipping {Event} — KitchenTicket for Order {OrderId} not found.",
                nameof(OrderCancelRejectedIntegrationEvent),
                message.OrderId);
            return;
        }

        ticket.ClearCancelRequest();

        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        await broadcaster.BroadcastTicketUpdatedAsync(ticket, context.CancellationToken);

        logger.LogInformation(
            "Cleared cancellation request for KitchenTicket {TicketId} (Order {OrderId}).",
            ticket.Id.Value,
            message.OrderId);
    }
}
