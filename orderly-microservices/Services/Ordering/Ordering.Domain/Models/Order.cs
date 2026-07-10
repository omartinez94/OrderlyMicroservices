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
    public string? CancellationReason { get; set; }
    public Instant? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
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

    /// <summary>
    /// Updates the customer-editable parts of an order (billing, delivery,
    /// payment). <c>Status</c> transitions are not handled here — use
    /// <see cref="Confirm"/>, <see cref="MarkReady"/>, <see cref="Cancel"/>
    /// for state changes so the legal-transition guards apply.
    /// </summary>
    public void Update(
        Address billingAddress,
        Address deliveryAddress,
        Payment payment)
    {
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(deliveryAddress);
        ArgumentNullException.ThrowIfNull(payment);

        BillingAddress = billingAddress;
        DeliveryAddress = deliveryAddress;
        Payment = payment;

        AddDomainEvent(new OrderUpdatedEvent(this));
    }

    /// <summary>
    /// <c>Pending -&gt; Confirmed</c>. Records the staff user that accepted
    /// the order and the moment of confirmation. The corresponding
    /// <see cref="OrderConfirmedEvent"/> is raised for downstream consumers
    /// (e.g. customer notification).
    /// </summary>
    public void Confirm(Guid confirmedByUserId, Instant now)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderStateTransitionException(Status, nameof(Confirm));

        if (confirmedByUserId == Guid.Empty)
            throw new ArgumentException("confirmedByUserId cannot be empty.", nameof(confirmedByUserId));

        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
        ConfirmedByUserId = confirmedByUserId;

        AddDomainEvent(new OrderConfirmedEvent(this, confirmedByUserId, now));
    }

    /// <summary>
    /// <c>Preparing -&gt; Ready</c>. Triggered by the kitchen's
    /// <c>KitchenOrderReadyIntegrationEvent</c>.
    /// </summary>
    public void MarkReady(Instant now)
    {
        if (Status != OrderStatus.Preparing)
            throw new InvalidOrderStateTransitionException(Status, nameof(MarkReady));

        Status = OrderStatus.Ready;
        ReadyAt = now;

        AddDomainEvent(new OrderReadyEvent(this, now));
    }

    /// <summary>
    /// <c>Confirmed -&gt; Preparing</c>. Triggered when the kitchen signals
    /// the first item started prep (today via the
    /// <c>POST /orders/{id}/start-prep</c> REST endpoint — no inbound event
    /// from Kitchen yet, see KITCHEN_FOLLOWUP_PLAN.md §7.1 open question).
    /// </summary>
    public void MarkPreparing(Instant now)
    {
        if (Status != OrderStatus.Confirmed)
            throw new InvalidOrderStateTransitionException(Status, nameof(MarkPreparing));

        Status = OrderStatus.Preparing;
        PreparingStartedAt = now;

        AddDomainEvent(new OrderPreparingEvent(this, now));
    }

    /// <summary>
    /// <c>Ready -&gt; Dispatched</c> (sets <see cref="DeliveryStatus"/>
    /// to <c>Dispatched</c> when the courier picks up the order). The
    /// aggregate status stays at <c>Ready</c> for dine-in / takeout
    /// orders — only delivery orders move through this transition.
    /// </summary>
    public void StartDelivery()
    {
        if (Status != OrderStatus.Ready)
            throw new InvalidOrderStateTransitionException(Status, nameof(StartDelivery));

        DeliveryStatus = BuildingBlocks.Enums.DeliveryStatus.Dispatched;
        AddDomainEvent(new OrderDeliveryStartedEvent(this));
    }

    /// <summary>
    /// <c>Ready -&gt; Delivered</c>. For delivery orders this should be
    /// preceded by <see cref="StartDelivery"/>; for dine-in / takeout the
    /// order moves straight from Ready to Delivered. The
    /// <see cref="DeliveryStatus"/> is stamped to <c>Delivered</c> for
    /// audit even when the order type is non-delivery.
    /// </summary>
    public void MarkDelivered(Instant now)
    {
        if (Status != OrderStatus.Ready)
            throw new InvalidOrderStateTransitionException(Status, nameof(MarkDelivered));

        Status = OrderStatus.Delivered;
        DeliveryStatus = BuildingBlocks.Enums.DeliveryStatus.Delivered;
        DeliveredAt = now;

        AddDomainEvent(new OrderDeliveredEvent(this, now));
    }

    /// <summary>
    /// <c>Delivered -&gt; Completed</c>. Closes out the order once the
    /// customer / cashier confirms receipt.
    /// </summary>
    public void Complete(Instant now)
    {
        if (Status != OrderStatus.Delivered)
            throw new InvalidOrderStateTransitionException(Status, nameof(Complete));

        Status = OrderStatus.Completed;
        CompletedAt = now;

        AddDomainEvent(new OrderCompletedEvent(this, now));
    }

    /// <summary>
    /// Permitted from any non-terminal state. Records reason and the user
    /// who cancelled for audit.
    /// </summary>
    public void Cancel(string reason, Guid cancelledByUserId, Instant now)
    {
        if (Status == OrderStatus.Cancelled
            || Status == OrderStatus.Completed
            || Status == OrderStatus.Delivered)
        {
            throw new InvalidOrderStateTransitionException(Status, nameof(Cancel));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (cancelledByUserId == Guid.Empty)
            throw new ArgumentException("cancelledByUserId cannot be empty.", nameof(cancelledByUserId));

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancelledByUserId = cancelledByUserId;
        CancellationReason = reason;

        AddDomainEvent(new OrderCancelledEvent(this, reason, cancelledByUserId, now));
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
