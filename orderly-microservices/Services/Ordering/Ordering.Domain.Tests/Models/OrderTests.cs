namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers every public entry point on <see cref="Order"/>: <c>Create</c>, <c>Update</c>,
/// <c>Add</c>, and <c>Remove</c>. The Order aggregate is the largest and most
/// event-emitting model in the domain, so this is where regressions hurt most.
/// </summary>
public sealed class OrderTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    // -------- Create --------

    /// <summary>
    /// Happy path: all valid args produce an order with every field propagated,
    /// status defaulted to <see cref="OrderStatus.Pending"/>, and exactly one
    /// <see cref="OrderCreatedEvent"/> referencing the new aggregate. This locks in
    /// both the field mapping and the domain-event contract — handlers and projection
    /// writers downstream depend on receiving that event.
    /// </summary>
    [Fact]
    public void Create_WithValidArgs_ReturnsOrderWithPendingStatusAndEvent()
    {
        var orderId = NewOrderId();
        var customerId = NewCustomerId();
        var orderNumber = ValidOrderNumber();
        var billing = ValidAddress();
        var delivery = ValidAddress();
        var payment = ValidPayment();
        var restaurantId = Guid.NewGuid();

        var order = Order.Create(orderId, customerId, orderNumber, restaurantId, billing, delivery, payment);

        order.Id.Should().Be(orderId);
        order.CustomerId.Should().Be(customerId);
        order.OrderNumber.Should().Be(orderNumber);
        order.RestaurantId.Should().Be(restaurantId);
        order.BillingAddress.Should().Be(billing);
        order.DeliveryAddress.Should().Be(delivery);
        order.Payment.Should().Be(payment);
        order.Status.Should().Be(OrderStatus.Pending);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderCreatedEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Null guard: <see cref="ArgumentNullException"/> is thrown when billing address
    /// is null, with the parameter name identifying the offending field.
    /// </summary>
    [Fact]
    public void Create_WithNullBillingAddress_Throws()
    {
        Action act = () => Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), null!, ValidAddress(), ValidPayment());

        act.Should().Throw<ArgumentNullException>().WithParameterName("billingAddress");
    }

    /// <summary>
    /// Null guard: <see cref="ArgumentNullException"/> is thrown when delivery address
    /// is null.
    /// </summary>
    [Fact]
    public void Create_WithNullDeliveryAddress_Throws()
    {
        Action act = () => Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), null!, ValidPayment());

        act.Should().Throw<ArgumentNullException>().WithParameterName("deliveryAddress");
    }

    /// <summary>
    /// Null guard: <see cref="ArgumentNullException"/> is thrown when payment is null.
    /// </summary>
    [Fact]
    public void Create_WithNullPayment_Throws()
    {
        Action act = () => Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("payment");
    }

    // -------- Update --------

    /// <summary>
    /// <c>Update</c> mutates the customer-editable fields (billing, delivery,
    /// payment) and raises exactly one <see cref="OrderUpdatedEvent"/>
    /// referencing this aggregate. Status is no longer mutated here — use
    /// <c>Confirm</c>, <c>MarkReady</c>, or <c>Cancel</c> for state changes.
    /// The pre-existing <c>OrderCreatedEvent</c> is cleared first so the
    /// assertion focuses on the event raised by <c>Update</c> alone.
    /// </summary>
    [Fact]
    public void Update_AssignsFieldsAndRaisesOrderUpdatedEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        // Clear the OrderCreatedEvent so we can assert only the Update event was raised next.
        order.ClearDomainEvents();

        var newBilling = Address.Of("1 New St", "Chicago", "IL", "67890", "US");
        var newDelivery = Address.Of("2 New St", "Chicago", "IL", "67890", "US");
        var newPayment = Payment.Of("Jane Doe", "5555555555554444", "01/31", "321", "Debit");
        var originalStatus = order.Status;

        order.Update(newBilling, newDelivery, newPayment);

        order.BillingAddress.Should().Be(newBilling);
        order.DeliveryAddress.Should().Be(newDelivery);
        order.Payment.Should().Be(newPayment);
        order.Status.Should().Be(originalStatus);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderUpdatedEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Null guard: <c>Update</c> rejects a null billing address.
    /// </summary>
    [Fact]
    public void Update_WithNullBillingAddress_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Update(null!, ValidAddress(), ValidPayment());

        act.Should().Throw<ArgumentNullException>().WithParameterName("billingAddress");
    }

    /// <summary>
    /// Null guard: <c>Update</c> rejects a null delivery address.
    /// </summary>
    [Fact]
    public void Update_WithNullDeliveryAddress_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Update(ValidAddress(), null!, ValidPayment());

        act.Should().Throw<ArgumentNullException>().WithParameterName("deliveryAddress");
    }

    /// <summary>
    /// Null guard: <c>Update</c> rejects a null payment.
    /// </summary>
    [Fact]
    public void Update_WithNullPayment_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Update(ValidAddress(), ValidAddress(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("payment");
    }

    // -------- Confirm --------

    /// <summary>
    /// Happy path: <c>Confirm</c> transitions <c>Pending -&gt; Confirmed</c>,
    /// stamps the audit fields, and raises exactly one
    /// <see cref="OrderConfirmedEvent"/>.
    /// </summary>
    [Fact]
    public void Confirm_FromPending_TransitionsToConfirmedAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Confirm(staffId, now);

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.ConfirmedAt.Should().Be(now);
        order.ConfirmedByUserId.Should().Be(staffId);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderConfirmedEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Illegal transition: <c>Confirm</c> on a non-<c>Pending</c> order
    /// raises <see cref="InvalidOrderStateTransitionException"/>. This is the
    /// guard the downstream consumer relies on — if the aggregate is already
    /// in a terminal-ish state, the consumer must nack and let the broker
    /// retry rather than silently rewriting Status.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Cancelled)]
    public void Confirm_FromNonPending_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        // Drive the order into the desired starting status via the legal
        // transition methods (rather than a back-door Status setter).
        switch (from)
        {
            case OrderStatus.Confirmed:
                order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
                break;
            case OrderStatus.Cancelled:
                order.Cancel("test", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
                break;
            // Preparing / Ready cannot be reached from Pending via the public
            // API alone (the kitchen-side consumers drive them); set Status
            // directly here only to seed the test fixture.
            default:
                order.GetType().GetProperty(nameof(Order.Status))!
                    .SetValue(order, from);
                break;
        }

        Action act = () => order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    /// <summary>
    /// Null guard: <c>Confirm</c> rejects an empty staff user id.
    /// </summary>
    [Fact]
    public void Confirm_WithEmptyStaffId_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Confirm(Guid.Empty, SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<ArgumentException>().WithParameterName("confirmedByUserId");
    }

    // -------- MarkReady --------

    /// <summary>
    /// Happy path: <c>MarkReady</c> transitions <c>Preparing -&gt; Ready</c>,
    /// stamps the audit field, and raises exactly one
    /// <see cref="OrderReadyEvent"/>.
    /// </summary>
    [Fact]
    public void MarkReady_FromPreparing_TransitionsToReadyAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        // Seed Preparing state via reflection (no public path exists from
        // Pending; the kitchen-side consumer is the production trigger).
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Preparing);
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkReady(now);

        order.Status.Should().Be(OrderStatus.Ready);
        order.ReadyAt.Should().Be(now);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderReadyEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Illegal transition: <c>MarkReady</c> only allows
    /// <c>Preparing -&gt; Ready</c>. Any other starting state must throw.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkReady_FromNonPreparing_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.MarkReady(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- Cancel --------

    /// <summary>
    /// Happy path: <c>Cancel</c> from <c>Pending</c> transitions to
    /// <c>Cancelled</c>, captures the reason and the cancelling user id, and
    /// raises <see cref="OrderCancelledEvent"/>.
    /// </summary>
    [Fact]
    public void Cancel_FromPending_TransitionsToCancelledAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Cancel("customer requested", staffId, now);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledAt.Should().Be(now);
        order.CancelledByUserId.Should().Be(staffId);
        order.CancellationReason.Should().Be("customer requested");
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderCancelledEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// <c>Cancel</c> is permitted from any non-terminal state. Verifies the
    /// transition from <c>Confirmed</c> for completeness.
    /// </summary>
    [Fact]
    public void Cancel_FromConfirmed_IsAllowed()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();

        order.Cancel("customer requested", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderCancelledEvent>();
    }

    /// <summary>
    /// Illegal transition: <c>Cancel</c> cannot be invoked on an already
    /// <c>Cancelled</c>, <c>Completed</c>, or <c>Delivered</c> order.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Delivered)]
    public void Cancel_FromTerminalState_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.Cancel("reason", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    /// <summary>
    /// Null/empty guard: <c>Cancel</c> rejects an empty reason string.
    /// </summary>
    [Fact]
    public void Cancel_WithEmptyReason_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Cancel("", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    /// <summary>
    /// Null guard: <c>Cancel</c> rejects an empty staff user id.
    /// </summary>
    [Fact]
    public void Cancel_WithEmptyUserId_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Cancel("reason", Guid.Empty, SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<ArgumentException>().WithParameterName("cancelledByUserId");
    }

    // -------- Add --------

    /// <summary>
    /// Happy path: <c>Add</c> appends a new <c>OrderItem</c> with the supplied
    /// <c>MenuItemId</c>, quantity, and unit price. Confirms the read-only
    /// <c>OrderItems</c> collection exposes the new item.
    /// </summary>
    [Fact]
    public void Add_AppendsOrderItemToOrderItems()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var menuItemId = NewMenuItemId();

        order.Add(menuItemId, quantity: 2, price: 9.99m);

        order.OrderItems.Should().HaveCount(1);
        var item = order.OrderItems.Single();
        item.MenuItemId.Should().Be(menuItemId);
        item.Quantity.Should().Be(2);
        item.UnitPrice.Should().Be(9.99m);
    }

    /// <summary>
    /// Null guard: <c>Add</c> rejects a null menu-item id.
    /// </summary>
    [Fact]
    public void Add_WithNullMenuItemId_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Add(null!, quantity: 1, price: 5m);

        act.Should().Throw<ArgumentNullException>().WithParameterName("menuItemId");
    }

    /// <summary>
    /// Documents the current no-dedup behavior: calling <c>Add</c> twice with the
    /// same <c>MenuItemId</c> produces two separate line items. If/when the team
    /// adds deduplication (e.g. incrementing the existing item's quantity), this
    /// test should be updated to assert <c>OrderItems</c> has count == 1 and the
    /// quantity is summed.
    /// </summary>
    [Fact]
    public void Add_TwiceWithSameMenuItemId_AppendsTwoSeparateItems()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var menuItemId = NewMenuItemId();

        order.Add(menuItemId, quantity: 1, price: 5m);
        order.Add(menuItemId, quantity: 2, price: 5m);

        order.OrderItems.Should().HaveCount(2);
    }

    // -------- Remove --------

    /// <summary>
    /// Happy path: <c>Remove</c> deletes the matching line item, leaving the
    /// collection empty.
    /// </summary>
    [Fact]
    public void Remove_ExistingItem_RemovesFromOrderItems()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var menuItemId = NewMenuItemId();
        order.Add(menuItemId, quantity: 1, price: 5m);

        order.Remove(menuItemId);

        order.OrderItems.Should().BeEmpty();
    }

    /// <summary>
    /// Documents that <c>Remove</c> is a no-op when the menu-item id is unknown —
    /// no exception, and the existing items are preserved. This keeps <c>Remove</c>
    /// idempotent and safe to call after a stale "remove" instruction.
    /// </summary>
    [Fact]
    public void Remove_NonExistingItem_DoesNotThrow_AndLeavesCollectionUnchanged()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var existing = NewMenuItemId();
        var nonExisting = NewMenuItemId();
        order.Add(existing, quantity: 1, price: 5m);

        order.Remove(nonExisting);

        order.OrderItems.Should().HaveCount(1);
        order.OrderItems.Single().MenuItemId.Should().Be(existing);
    }

    /// <summary>
    /// Null guard: <c>Remove</c> rejects a null menu-item id.
    /// </summary>
    [Fact]
    public void Remove_WithNullMenuItemId_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.Remove(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("menuItemId");
    }
}