namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a single item on a <c>KitchenTicket</c> moves from
/// <c>Preparing</c> to <c>Ready</c>.
/// </summary>
public record KitchenTicketItemReadyEvent(
    KitchenTicket Ticket,
    KitchenItemId ItemId,
    Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}