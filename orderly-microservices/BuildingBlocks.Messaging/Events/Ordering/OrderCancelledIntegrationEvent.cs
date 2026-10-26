namespace BuildingBlocks.Messaging.Events.Ordering;

/// <summary>
/// Published by Ordering when an order is cancelled directly or via cancellation approval.
/// Consumed by Kitchen to transition the KDS ticket to <c>CancelledPendingAck</c>
/// so kitchen staff acknowledge external cancellation before the ticket leaves the screen.
/// </summary>
public record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid RestaurantId { get; init; }
    public string OrderNumber { get; init; } = default!;
    public Instant CancelledAt { get; init; }
    public Guid CancelledByUserId { get; init; }
    public string Reason { get; init; } = default!;
}
