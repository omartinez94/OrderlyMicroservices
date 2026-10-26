namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when a bumped <c>KitchenTicket</c> is recalled back
/// to <c>Ready</c> by chef action (<c>Bumped</c> → <c>Ready</c>). Consumed
/// by Ordering to revert the order status from <c>Delivered</c> back to
/// <c>Ready</c>.
/// </summary>
public record KitchenOrderRecalledIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid KitchenTicketId { get; init; }
    public Guid RestaurantId { get; init; }
    public Guid RecalledByUserId { get; init; }
    public Instant RecalledAt { get; init; }
}
