namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.Complete"/> when an order transitions
/// <c>Delivered -&gt; Completed</c>.
/// </summary>
public record OrderCompletedEvent(Order Order, Instant CompletedAt) : IDomainEvent;