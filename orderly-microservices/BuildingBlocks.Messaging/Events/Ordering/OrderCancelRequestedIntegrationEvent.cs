namespace BuildingBlocks.Messaging.Events.Ordering;

/// <summary>
/// Published by Ordering when staff requests cancellation of an in-progress order
/// (<c>Confirmed</c>, <c>Preparing</c>, or <c>Ready</c>). Consumed by Kitchen to
/// flag the ticket as cancel-requested on the KDS display.
/// </summary>
public record OrderCancelRequestedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid RestaurantId { get; init; }
    public string OrderNumber { get; init; } = default!;
    public Instant RequestedAt { get; init; }
    public Guid RequestedByUserId { get; init; }
    public string Reason { get; init; } = default!;
}
