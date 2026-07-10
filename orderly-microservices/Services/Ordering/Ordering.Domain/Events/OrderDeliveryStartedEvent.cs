namespace Ordering.Domain.Events;

/// <summary>
/// Raised by <see cref="Models.Order.StartDelivery"/> when a delivery
/// order leaves <c>Ready</c> and the courier picks it up. Stamps the
/// <c>DeliveryStatus</c> to <c>Dispatched</c>.
/// </summary>
public record OrderDeliveryStartedEvent(Order Order) : IDomainEvent;