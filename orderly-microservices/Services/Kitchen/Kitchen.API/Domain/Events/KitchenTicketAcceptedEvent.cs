namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a <c>KitchenTicket</c> transitions from <c>New</c> to
/// <c>InProgress</c> via <c>KitchenTicket.Accept</c>.
/// </summary>
public record KitchenTicketAcceptedEvent(
    KitchenTicket Ticket,
    Guid AcceptedByUserId,
    Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}