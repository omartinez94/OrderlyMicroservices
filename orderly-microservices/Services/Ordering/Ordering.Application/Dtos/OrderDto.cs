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
    IReadOnlyList<OrderItemDto> OrderItems
);

