namespace Ordering.Domain.Models;

public class Order : Aggregate<OrderId>
{
    public int ActualPrepTimeMinutes { get; set; }
    public Address BillingAddress { get; set; } = default!;
    public string Currency { get; set; } = string.Empty;
    public CustomerId CustomerId { get; set; } = default!;
    public Address DeliveryAddress { get; set; } = default!;
    public string DeliveryNotes { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public string DiscountCode { get; set; } = string.Empty;
    public int EstimatedPrepTimeMinutes { get; set; }
    public bool IsModified { get; set; }
    public string Notes { get; set; } = string.Empty;
    public OrderNumber OrderNumber { get; set; } = default!;
    /// <summary>Type of the order: dine-in, takeout, delivery</summary>
    public OrderType OrderType { get; set; } = OrderType.DineIn;
    public Payment Payment { get; set; } = default!;
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

    // Navigation properties
    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public static Order Create(
        OrderId id,
        CustomerId customerId,
        OrderNumber orderNumber,
        Guid restaurantId,
        Address billingAddress,
        Address deliveryAddress,
        Payment payment)
    {
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(deliveryAddress);
        ArgumentNullException.ThrowIfNull(payment);

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            OrderNumber = orderNumber,
            RestaurantId = restaurantId,
            BillingAddress = billingAddress,
            DeliveryAddress = deliveryAddress,
            Payment = payment,
            Status = OrderStatus.Pending
        };

        // Add domain event
        order.AddDomainEvent(new OrderCreatedEvent(order));

        return order;
    }

    public void Update(
        Address billingAddress,
        Address deliveryAddress,
        Payment payment,
        OrderStatus status)
    {
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(deliveryAddress);
        ArgumentNullException.ThrowIfNull(payment);

        BillingAddress = billingAddress;
        DeliveryAddress = deliveryAddress;
        Payment = payment;
        Status = status;

        // AddDomainEvent(new OrderUpdatedEvent(this));
    }

    public void Add(MenuItemId menuItemId, int quantity, decimal price)
    {
        ArgumentNullException.ThrowIfNull(menuItemId);

        var orderItem = new OrderItem(Id, menuItemId, quantity, price);

        _orderItems.Add(orderItem);
    }

    public void Remove(MenuItemId menuItemId)
    {
        ArgumentNullException.ThrowIfNull(menuItemId);

        var orderItem = _orderItems.FirstOrDefault(x => x.MenuItemId == menuItemId);

        if (orderItem is not null)
        {
            _orderItems.Remove(orderItem);
        }
    }
}
