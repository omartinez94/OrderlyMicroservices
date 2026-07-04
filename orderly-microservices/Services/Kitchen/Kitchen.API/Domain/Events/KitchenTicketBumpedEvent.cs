namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a <c>KitchenTicket</c> transitions from <c>Ready</c> to
/// <c>Bumped</c> — the expo pass has acknowledged the ticket.
/// </summary>
public record KitchenTicketBumpedEvent(KitchenTicket Ticket, Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}