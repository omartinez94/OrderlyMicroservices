namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.Confirm"/> when an order transitions
/// <c>Pending -&gt; Confirmed</c>. Carries the staff user id that
/// acknowledged the order and the moment confirmation happened.
/// </summary>
public record OrderConfirmedEvent(
    Order Order,
    Guid ConfirmedByUserId,
    Instant ConfirmedAt) : IDomainEvent;