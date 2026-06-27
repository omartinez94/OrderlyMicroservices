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
    /// <c>Update</c> mutates the four target fields (billing, delivery, payment, status)
    /// and raises exactly one <see cref="OrderUpdatedEvent"/> referencing this aggregate.
    /// The pre-existing <c>OrderCreatedEvent</c> is cleared first so the assertion focuses
    /// on the event raised by <c>Update</c> alone.
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
        var newStatus = OrderStatus.Confirmed;

        order.Update(newBilling, newDelivery, newPayment, newStatus);

        order.BillingAddress.Should().Be(newBilling);
        order.DeliveryAddress.Should().Be(newDelivery);
        order.Payment.Should().Be(newPayment);
        order.Status.Should().Be(newStatus);
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

        Action act = () => order.Update(null!, ValidAddress(), ValidPayment(), OrderStatus.Confirmed);

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

        Action act = () => order.Update(ValidAddress(), null!, ValidPayment(), OrderStatus.Confirmed);

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

        Action act = () => order.Update(ValidAddress(), ValidAddress(), null!, OrderStatus.Confirmed);

        act.Should().Throw<ArgumentNullException>().WithParameterName("payment");
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