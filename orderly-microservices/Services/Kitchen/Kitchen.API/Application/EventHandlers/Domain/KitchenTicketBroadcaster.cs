namespace Kitchen.API.Application.EventHandlers.Domain;

/// <summary>
/// Single in-process handler that maps every <see cref="IDomainEvent"/> raised
/// by <see cref="Aggregates.KitchenTicket.KitchenTicket"/> to a SignalR
/// broadcast on the right <c>restaurant:{id}</c> group. The
/// <see cref="DispatchDomainEventsInterceptor"/> drains domain events before
/// <c>SaveChanges</c> commits, so this handler runs while the EF transaction
/// is still open — the UI sees the change the instant the database commits.
///
/// <c>OrderCreated</c> is intentionally NOT handled here: it is fired by the
/// inbound <c>OrderCreatedIntegrationEventHandler</c> after it builds the
/// ticket, which broadcasts directly via the same hub context.
/// </summary>
public class KitchenTicketBroadcaster(IHubContext<KitchenHub, IKitchenHubClient> hub)
    : INotificationHandler<IDomainEvent>
{
    public async Task Handle(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case KitchenTicketAcceptedEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .TicketAccepted(e.Ticket.Id.Value, e.AcceptedByUserId, e.OccurredOn);
                break;

            case KitchenTicketItemPrepStartedEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .ItemStateChanged(e.Ticket.Id.Value, e.ItemId.Value, nameof(KitchenItemStatus.Preparing));
                break;

            case KitchenTicketItemReadyEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .ItemStateChanged(e.Ticket.Id.Value, e.ItemId.Value, nameof(KitchenItemStatus.Ready));
                break;

            case KitchenTicketReadyEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .OrderReady(e.Ticket.Id.Value, e.OccurredOn);
                break;

            case KitchenTicketBumpedEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .OrderBumped(e.Ticket.Id.Value, e.OccurredOn);
                break;

            case KitchenTicketCancelledEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .OrderCancelled(e.Ticket.Id.Value, e.Reason);
                break;

            case KitchenTicketRecalledEvent e:
                await hub.Clients
                    .Group($"restaurant:{e.Ticket.RestaurantId}")
                    .TicketRecalled(e.Ticket.Id.Value);
                break;

            // Other domain event types (no broadcast — local-only side effects).
            default:
                break;
        }
    }
}