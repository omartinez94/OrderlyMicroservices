namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Kitchen when the first item of a <c>KitchenTicket</c> moves
/// into <c>Preparing</c>. The aggregate records the same boundary via
/// <see cref="Kitchen.API.Domain.Events.KitchenTicketItemPrepStartedEvent"/>,
/// but the application handler publishes this integration event only once per
/// ticket — on the transition from <c>New</c> to "first item preparing".
/// Consumed by Ordering to drive <c>Order.MarkPreparing</c> (the production
/// path that supersedes the manual <c>POST /orders/{id}/start-prep</c>
/// override).
/// </summary>
public record KitchenOrderPrepStartedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid ItemId { get; init; }
    public Guid StaffUserId { get; init; }
    public Instant StartedAt { get; init; }
}