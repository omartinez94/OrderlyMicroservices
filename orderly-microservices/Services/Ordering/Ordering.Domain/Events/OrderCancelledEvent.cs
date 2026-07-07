namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.Cancel"/> when an order moves from any
/// non-terminal state to <c>Cancelled</c>. Carries the cancellation reason,
/// the user id that performed the cancellation, and the moment it occurred.
/// </summary>
public record OrderCancelledEvent(
    Order Order,
    string Reason,
    Guid CancelledByUserId,
    Instant CancelledAt) : IDomainEvent;