namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a single item on a <c>KitchenTicket</c> moves from
/// <c>Pending</c> to <c>Preparing</c>.
/// </summary>
public record KitchenTicketItemPrepStartedEvent(
    KitchenTicket Ticket,
    KitchenItemId ItemId,
    Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}