namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when kitchen staff reject an order cancellation request on the KDS.
/// Consumed by Ordering to restore the order to its previous status.
/// </summary>
public record KitchenOrderCancelRejectedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid RestaurantId { get; init; }
    public string OrderNumber { get; init; } = default!;
    public Instant RejectedAt { get; init; }
    public string? RejectedByUserId { get; init; }
    public string? Reason { get; init; }
}
