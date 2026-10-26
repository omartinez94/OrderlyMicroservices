namespace BuildingBlocks.Messaging.Events.Ordering;

/// <summary>
/// Published by Ordering when an approver rejects a pending cancellation request.
/// Consumed by Kitchen to clear the cancel-requested flag on the KDS ticket.
/// </summary>
public record OrderCancelRejectedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid RestaurantId { get; init; }
    public string OrderNumber { get; init; } = default!;
    public Instant RejectedAt { get; init; }
    public Guid RejectedByUserId { get; init; }
    public string Reason { get; init; } = default!;
    public string RestoredStatus { get; init; } = default!;
}
