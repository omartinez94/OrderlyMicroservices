namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a <c>KitchenTicket</c> is recalled from <c>Bumped</c> back to
/// <c>Ready</c> — the chef pulled the ticket back.
/// </summary>
public record KitchenTicketRecalledEvent(KitchenTicket Ticket, Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}