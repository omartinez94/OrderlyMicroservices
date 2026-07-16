namespace Ordering.Domain.Models;

/// <summary>
/// Typed payload carried on an <see cref="OrderActivity"/> row that
/// records the status transition driving the activity. Each prev/new
/// pair is populated ONLY on the matching transition type; all other
/// fields stay <c>null</c>. The full record is serialised as JSON into
/// the <c>order_activities.Metadata</c> column by the EF Core
/// configuration.
/// </summary>
/// <remarks>
/// The shape is intentionally narrow — the wire DTO is not modelled here.
/// Adding a new status enum pair is a wire-breaking change to the jsonb
/// column; treat the field set as locked for v1.
/// </remarks>
public record OrderActivityMetadata(
    string? Reason,
    Guid? OrderItemId,
    string? OrderItemName,
    OrderStatus? PreviousOrderStatus,
    OrderStatus? NewOrderStatus,
    PrepStatus? PreviousPrepStatus,
    PrepStatus? NewPrepStatus,
    DeliveryStatus? PreviousDeliveryStatus,
    DeliveryStatus? NewDeliveryStatus);