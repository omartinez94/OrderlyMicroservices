namespace Catalog.API.Models;

public class Order : AuditableEntity<Guid>
{
    public int ActualPrepTimeMinutes { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string DeliveryNotes { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public string DiscountCode { get; set; } = string.Empty;
    public int EstimatedPrepTimeMinutes { get; set; }
    public bool IsModified { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    /// <summary>Type of the order: dine-in, takeout, delivery</summary>
    public OrderType OrderType { get; set; } = OrderType.DineIn;
    public bool RequiresAdminApproval { get; set; }
    public Guid RestaurantId { get; set; }
    /// <summary>Current state: ordering, pending, confirmed, preparing, ready, delivered, completed, cancelled, on_hold</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    /// <summary>Snapshot of the total subtotal at order time</summary>
    public decimal Subtotal { get; set; }
    /// <summary>Snapshot of the calculated tax</summary>
    public decimal TaxAmount { get; set; }
    /// <summary>Snapshot of the active tax rate when the order was placed</summary>
    public decimal TaxRate { get; set; }
    /// <summary>Final calculated total amount</summary>
    public decimal TotalAmount { get; set; }
    public Instant? ApprovedAt { get; set; }
    public Guid? ApprovedByAdminId { get; set; }
    public Instant? CancelledAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public Instant? ConfirmedAt { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Instant? DeliveredAt { get; set; }
    public decimal? DeliveryLatitude { get; set; }
    public decimal? DeliveryLongitude { get; set; }
    public DeliveryStatus? DeliveryStatus { get; set; }
    public Instant? PreparingStartedAt { get; set; }
    public Instant? ReadyAt { get; set; }
    public Guid? TableId { get; set; }
}
