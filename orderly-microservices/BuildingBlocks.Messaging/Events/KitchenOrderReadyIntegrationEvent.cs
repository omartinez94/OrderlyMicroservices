namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when a <c>KitchenTicket</c> reaches the aggregate
/// <c>Ready</c> state (every item is ready). Consumed by Ordering to
/// transition the upstream <c>Order</c> to <c>Ready</c> via
/// <c>Order.MarkReady</c>.
/// </summary>
public record KitchenOrderReadyIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Instant ReadyAt { get; init; }
}