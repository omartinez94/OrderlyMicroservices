namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.MarkDelivered"/> when an order
/// transitions <c>Ready -&gt; Delivered</c>.
/// </summary>
public record OrderDeliveredEvent(Order Order, Instant DeliveredAt) : IDomainEvent;