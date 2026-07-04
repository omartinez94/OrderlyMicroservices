namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a <c>KitchenTicket</c> transitions to <c>Ready</c>. Fires only
/// after every item has reached <c>KitchenItemStatus.Ready</c>.
/// </summary>
public record KitchenTicketReadyEvent(KitchenTicket Ticket, Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}