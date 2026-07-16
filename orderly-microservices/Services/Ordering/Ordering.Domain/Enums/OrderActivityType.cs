namespace Ordering.Domain.Enums;

/// <summary>
/// Every event the activity feed records for an
/// <see cref="Models.Order"/>: aggregate-level state transitions,
/// customer edits, and per-item prep transitions. New values must be
/// appended (do NOT renumber existing ones — the enum is stored as a
/// <c>nvarchar(50)</c> string for human readability).
/// </summary>
public enum OrderActivityType
{
    /// <summary>The order was created from a basket checkout.</summary>
    OrderCreated = 0,

    /// <summary>The customer edited billing/delivery/payment.</summary>
    OrderUpdated = 1,

    /// <summary>Pending → Confirmed.</summary>
    OrderConfirmed = 2,

    /// <summary>Confirmed → Preparing.</summary>
    OrderPreparingStarted = 3,

    /// <summary>Preparing → Ready.</summary>
    OrderReady = 4,

    /// <summary>Delivery courier picked up the order (Ready + DeliveryStatus=Dispatched).</summary>
    OrderDeliveryStarted = 5,

    /// <summary>Ready → Delivered.</summary>
    OrderDelivered = 6,

    /// <summary>Delivered → Completed.</summary>
    OrderCompleted = 7,

    /// <summary>Cancelled (from any non-terminal state).</summary>
    OrderCancelled = 8,

    /// <summary>Per-item prep started.</summary>
    OrderItemPrepStarted = 9,

    /// <summary>Per-item prep completed.</summary>
    OrderItemPrepCompleted = 10,
}