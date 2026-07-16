namespace Ordering.Application.Dtos;

public record OrderDto(
    Guid Id,
    Guid CustomerId,
    string OrderNumber,
    Guid RestaurantId,

    // Financials
    string Currency,
    decimal Subtotal,
    decimal TaxRate,
    decimal TaxAmount,
    decimal DiscountAmount,
    string DiscountCode,
    decimal TotalAmount,

    // Status & type
    OrderStatus Status,
    OrderType OrderType,

    // Delivery
    AddressDto BillingAddress,
    AddressDto DeliveryAddress,
    string DeliveryNotes,
    DeliveryStatus? DeliveryStatus,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,

    // Payment
    PaymentDto Payment,

    // Prep timing
    int EstimatedPrepTimeMinutes,
    int ActualPrepTimeMinutes,

    // Flags
    bool IsModified,
    bool RequiresAdminApproval,

    // Optional references
    Guid? TableId,
    Guid? CreatedByUserId,
    Guid? ApprovedByAdminId,
    Guid? ConfirmedByUserId,
    Guid? CompletedByUserId,

    // Lifecycle timestamps
    Instant? ApprovedAt,
    Instant? CancelledAt,
    Instant? CompletedAt,
    Instant? ConfirmedAt,
    Instant? DeliveredAt,
    Instant? PreparingStartedAt,
    Instant? ReadyAt,

    string Notes,
    IReadOnlyList<OrderItemDto> OrderItems,

    /// <summary>
    /// Chronological activity feed (one row per state transition + per-item
    /// prep transition). Ordered by <c>OccurredAt ASC, Id ASC</c> for
    /// stable rendering. Each row carries the actor id (where available)
    /// and the per-request <c>CorrelationId</c> so log-trace correlation
    /// works end-to-end.
    /// </summary>
    IReadOnlyList<OrderActivityDto> Activities
);

