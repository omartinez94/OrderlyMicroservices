namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when a <c>KitchenTicket</c> is cancelled. Consumed
/// by Ordering to cancel the upstream <c>Order</c> via
/// <c>Order.Cancel</c>.
/// </summary>
public record KitchenOrderCancelledIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid CancelledByUserId { get; init; }
    public Instant CancelledAt { get; init; }
}