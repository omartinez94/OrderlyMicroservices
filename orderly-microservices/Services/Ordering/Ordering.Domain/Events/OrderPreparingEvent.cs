namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.MarkPreparing"/> when an order
/// transitions <c>Confirmed -&gt; Preparing</c>.
/// </summary>
public record OrderPreparingEvent(Order Order, Instant StartedAt) : IDomainEvent;