namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Inbound consumer for <c>OrderCreatedIntegrationEvent</c>. Builds a
/// <c>KitchenTicket</c> from the event, persists it, and broadcasts
/// <c>OrderReceived</c> over the SignalR hub so subscribed kitchen displays
/// refresh immediately.
///
/// Idempotency is enforced in two places:
/// <list type="bullet">
///   <item>An optimistic <c>GetByIdAsync</c> pre-check (handles the
///         common case where the second event arrives after the first
///         consumer has already committed).</item>
///   <item>A <c>try/catch(DbUpdateException)</c> with the
///         <see cref="IsDuplicateKey.IsUniqueViolation"/> guard (handles
///         the rare race where two events pass the pre-check before
///         either commits — the loser observes a PG 23505 unique
///         violation and exits as a no-op instead of nacking).</item>
/// </list>
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

        // Idempotency (fast path): if a ticket already exists for this
        // order, skip. Re-deliveries from the broker are at-least-once.
        KitchenTicket? existing = await repository.GetByIdAsync(evt.OrderId, context.CancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "KitchenTicket for Order {OrderId} already exists; ignoring duplicate OrderCreatedIntegrationEvent {EventId}.",
                evt.OrderId,
                evt.Id);
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

        // Idempotency (race path): two concurrent consumers can both pass
        // the pre-check above; the loser will see a PostgreSQL
        // unique_violation (23505) on commit. Treat the collision as a
        // success signal — the winning consumer already created the
        // ticket, and the second event is a no-op. Re-throwing would
        // surface as a MassTransit nack and poison-message loop.
        try
        {
            await repository.AddAsync(ticket, context.CancellationToken);
            await unitOfWork.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey.IsUniqueViolation(ex))
        {
            logger.LogInformation(
                "Duplicate OrderCreatedIntegrationEvent {EventId} for Order {OrderId}; the racing consumer won the create, skipping.",
                evt.Id,
                evt.OrderId);
            return;
        }

        // Broadcast AFTER save so subscribers never see a ticket that wasn't
        // persisted. The DTO is mapped once here; per-item state changes go
        // through KitchenTicketBroadcaster for incremental updates.
        await hub.Clients
            .Group($"restaurant:{ticket.RestaurantId}")
            .OrderReceived(ticket.ToDto());

        logger.LogInformation(
            "Created KitchenTicket {TicketId} for Order {OrderNumber} ({ItemCount} items) from event {EventId}.",
            ticket.Id.Value,
            ticket.OrderNumber,
            ticket.Items.Count,
            evt.Id);
    }
}