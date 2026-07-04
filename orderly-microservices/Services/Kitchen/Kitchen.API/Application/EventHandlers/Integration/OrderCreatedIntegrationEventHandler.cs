namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Inbound consumer for <c>OrderCreatedIntegrationEvent</c>. Builds a
/// <c>KitchenTicket</c> from the event, persists it, and broadcasts
/// <c>OrderReceived</c> over the SignalR hub so subscribed kitchen displays
/// refresh immediately.
/// </summary>
public class OrderCreatedIntegrationEventHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IHubContext<KitchenHub, IKitchenHubClient> hub,
    ILogger<OrderCreatedIntegrationEventHandler> logger)
    : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OrderCreatedIntegrationEvent evt = context.Message;

        // Idempotency: if a ticket already exists for this order, skip.
        // Re-deliveries from the broker are at-least-once.
        KitchenTicket? existing = await repository.GetByIdAsync(evt.OrderId, context.CancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "KitchenTicket for Order {OrderId} already exists; ignoring duplicate OrderCreatedIntegrationEvent.",
                evt.OrderId);
            return;
        }

        Instant receivedAt = evt.OccurredOn == default
            ? SystemClock.Instance.GetCurrentInstant()
            : evt.OccurredOn;

        KitchenTicket ticket = KitchenTicket.CreateFromOrder(
            orderId: evt.OrderId,
            restaurantId: evt.RestaurantId,
            customerId: evt.CustomerId,
            orderNumber: evt.OrderNumber,
            itemSeeds: evt.ToOrderItemSeeds(),
            notes: evt.Notes,
            receivedAt: receivedAt);

        await repository.AddAsync(ticket, context.CancellationToken);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        // Broadcast AFTER save so subscribers never see a ticket that wasn't
        // persisted. The DTO is mapped once here; per-item state changes go
        // through KitchenTicketBroadcaster for incremental updates.
        await hub.Clients
            .Group($"restaurant:{ticket.RestaurantId}")
            .OrderReceived(ticket.ToDto());

        logger.LogInformation(
            "Created KitchenTicket {TicketId} for Order {OrderNumber} ({ItemCount} items).",
            ticket.Id.Value,
            ticket.OrderNumber,
            ticket.Items.Count);
    }
}