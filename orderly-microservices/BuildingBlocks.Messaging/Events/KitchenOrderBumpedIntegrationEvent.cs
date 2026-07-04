namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when a <c>KitchenTicket</c> is bumped (the expo pass
/// has acknowledged the ticket — <c>Ready</c> → <c>Bumped</c>). Not
/// currently consumed by Ordering but recorded for downstream analytics /
/// audit consumers to attach.
/// </summary>
public record KitchenOrderBumpedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid BumpedByUserId { get; init; }
    public Instant BumpedAt { get; init; }
}