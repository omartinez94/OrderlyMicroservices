namespace Kitchen.API.Domain.Events;

/// <summary>
/// Raised when a <c>KitchenTicket</c> is cancelled from any non-terminal
/// state. <c>Reason</c> and <c>CancelledByUserId</c> are recorded for audit.
/// </summary>
public record KitchenTicketCancelledEvent(
    KitchenTicket Ticket,
    string Reason,
    Guid CancelledByUserId,
    Instant OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}