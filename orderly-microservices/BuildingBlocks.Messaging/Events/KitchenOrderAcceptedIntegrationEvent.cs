namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when a <c>KitchenTicket</c> is accepted (status
/// moves from <c>New</c> to <c>InProgress</c>). Consumed by Ordering to
/// transition the upstream <c>Order</c> from <c>Pending</c> to
/// <c>Confirmed</c> via <c>Order.Confirm</c>.
/// </summary>
public record KitchenOrderAcceptedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid ConfirmedByUserId { get; init; }
    public Instant ConfirmedAt { get; init; }
}