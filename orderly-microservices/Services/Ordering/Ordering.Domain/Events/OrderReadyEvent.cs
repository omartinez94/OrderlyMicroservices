namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.MarkReady"/> when an order transitions
/// <c>Preparing -&gt; Ready</c>. Carries the moment the kitchen reported
/// the order ready for pickup / dispatch.
/// </summary>
public record OrderReadyEvent(Order Order, Instant ReadyAt) : IDomainEvent;