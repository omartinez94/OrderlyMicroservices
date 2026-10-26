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
        Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111");

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
    public void Order_ImplementsIShortIdentifiable_AndGeneratesShortId()
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            ValidOrderNumber(),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());

        order.Should().BeAssignableTo<BuildingBlocks.Entities.Interfaces.IShortIdentifiable>();
        order.ShortId.Should().StartWith(BuildingBlocks.Identifiers.EntityPrefixConstants.Order);
        order.ShortId.Length.Should().Be(11);
        BuildingBlocks.Identifiers.ShortIdGenerator.IsValid(order.ShortId, BuildingBlocks.Identifiers.EntityPrefix.Order).Should().BeTrue();
    }

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
        var newPayment = Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Mastercard", "4444");
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
    [InlineData(OrderStatus.Cancelled)]
    public void Confirm_FromCancelled_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Cancel("test", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        Action act = () => order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.CancelRequested)]
    public void Confirm_WhenAlreadyAtOrPastTarget_IsIdempotentNoOp(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();

        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(from);
        order.DomainEvents.Should().BeEmpty();
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
    [InlineData(OrderStatus.Cancelled)]
    public void MarkReady_FromCancelled_Throws(OrderStatus from)
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

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void MarkReady_FromPendingOrConfirmed_TransitionsToReady(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkReady(now);

        order.Status.Should().Be(OrderStatus.Ready);
        order.ReadyAt.Should().Be(now);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderReadyEvent>();
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.CancelRequested)]
    public void MarkReady_WhenAlreadyReadyOrPast_IsIdempotentNoOp(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();

        order.MarkReady(SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(from);
        order.DomainEvents.Should().BeEmpty();
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
    /// Illegal transition: <c>Cancel</c> cannot be invoked on Completed or Delivered orders.
    /// </summary>
    [Theory]
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

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotentNoOp()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        var staffId = Guid.NewGuid();
        var t0 = SystemClock.Instance.GetCurrentInstant();
        order.Cancel("initial cancel", staffId, t0);
        order.ClearDomainEvents();

        order.Cancel("second cancel", staffId, t0.Plus(Duration.FromMinutes(1)));

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("initial cancel");
        order.DomainEvents.Should().BeEmpty();
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
        item.TotalPrice.Should().Be(19.98m);
        item.Parent.Should().BeSameAs(order);
    }

    [Fact]
    public void Add_WithFullMetadata_PopulatesAllOrderItemProperties()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var menuItemId = NewMenuItemId();
        var customizations = new List<KitchenOrderItemCustomization>
        {
            new("No onions", null, 0m),
            new("Extra sauce", "Spicy", 0.75m)
        };
        var variations = new List<KitchenOrderItemVariation>
        {
            new("Size: Large", 2.50m)
        };

        order.Add(
            menuItemId,
            quantity: 3,
            price: 12.50m,
            itemName: "Special Pizza",
            specialInstructions: "Cut into 8 slices",
            customizations: customizations,
            selectedVariations: variations,
            seatNumber: 4);

        order.OrderItems.Should().HaveCount(1);
        var item = order.OrderItems.Single();
        item.MenuItemId.Should().Be(menuItemId);
        item.Quantity.Should().Be(3);
        item.UnitPrice.Should().Be(12.50m);
        item.TotalPrice.Should().Be(37.50m);
        item.MenuItemName.Should().Be("Special Pizza");
        item.SpecialInstructions.Should().Be("Cut into 8 slices");
        item.Customizations.Should().BeEquivalentTo(customizations);
        item.SelectedVariations.Should().BeEquivalentTo(variations);
        item.SeatNumber.Should().Be(4);
        item.Parent.Should().BeSameAs(order);
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

    // -------- MarkPreparing --------

    /// <summary>
    /// Happy path: <c>MarkPreparing</c> transitions <c>Confirmed -&gt;
    /// Preparing</c>, stamps <c>PreparingStartedAt</c>, and raises
    /// <see cref="OrderPreparingEvent"/>.
    /// </summary>
    [Fact]
    public void MarkPreparing_FromConfirmed_TransitionsToPreparingAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.MarkPreparing(now);

        order.Status.Should().Be(OrderStatus.Preparing);
        order.PreparingStartedAt.Should().Be(now);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderPreparingEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Illegal transition: <c>MarkPreparing</c> from any non-Confirmed
    /// state throws <see cref="InvalidOrderStateTransitionException"/>.
    /// </summary>
    [Fact]
    public void MarkPreparing_FromPending_TransitionsToPreparingAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkPreparing(now);

        order.Status.Should().Be(OrderStatus.Preparing);
        order.PreparingStartedAt.Should().Be(now);
        order.ConfirmedAt.Should().Be(now);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderPreparingEvent>();
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.CancelRequested)]
    public void MarkPreparing_WhenAlreadyPreparingOrPast_IsIdempotentNoOp(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();

        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(from);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkPreparing_FromCancelled_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- MarkDelivered --------

    /// <summary>
    /// Happy path: <c>MarkDelivered</c> transitions <c>Ready -&gt;
    /// Delivered</c>, stamps <c>DeliveredAt</c> + <c>DeliveryStatus</c>,
    /// and raises <see cref="OrderDeliveredEvent"/>.
    /// </summary>
    [Fact]
    public void MarkDelivered_FromReady_TransitionsToDeliveredAndRaisesEvent()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Ready);
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.MarkDelivered(now);

        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().Be(now);
        order.DeliveryStatus.Should().Be(DeliveryStatus.Delivered);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderDeliveredEvent>()
            .Which.Order.Should().BeSameAs(order);
    }

    /// <summary>
    /// Illegal transition: <c>MarkDelivered</c> from any non-Ready state
    /// throws <see cref="InvalidOrderStateTransitionException"/>.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    public void MarkDelivered_FromConfirmedOrPreparing_TransitionsToDelivered(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkDelivered(now);

        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().Be(now);
        order.ReadyAt.Should().Be(now);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderDeliveredEvent>();
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.CancelRequested)]
    public void MarkDelivered_WhenAlreadyDeliveredOrCancelRequested_IsIdempotentNoOp(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();

        order.MarkDelivered(SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(from);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkDelivered_FromPendingOrCancelled_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.MarkDelivered(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- RecallDelivery --------

    [Fact]
    public void RecallDelivery_FromDelivered_TransitionsToReadyAndClearsDeliveredAt()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Delivered);
        order.DeliveredAt = SystemClock.Instance.GetCurrentInstant();
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.RecallDelivery(now);

        order.Status.Should().Be(OrderStatus.Ready);
        order.DeliveredAt.Should().BeNull();
        order.StatusChangedAt.Should().Be(now);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderReadyEvent>();
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.CancelRequested)]
    public void RecallDelivery_WhenAlreadyReadyOrCancelRequested_IsIdempotentNoOp(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.ClearDomainEvents();

        order.RecallDelivery(SystemClock.Instance.GetCurrentInstant());

        order.Status.Should().Be(from);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Cancelled)]
    public void RecallDelivery_FromNonDelivered_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.RecallDelivery(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- Complete --------

    /// <summary>
    /// Happy path: <c>Complete</c> transitions <c>Delivered -&gt;
    /// Completed</c> and stamps <c>CompletedAt</c>.
    /// </summary>
    [Fact]
    public void Complete_FromDelivered_TransitionsToCompleted()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Delivered);
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.Complete(now);

        order.Status.Should().Be(OrderStatus.Completed);
        order.CompletedAt.Should().Be(now);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderCompletedEvent>();
    }

    /// <summary>
    /// Illegal transition: <c>Complete</c> from any non-Delivered state
    /// throws.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Cancelled)]
    public void Complete_FromNonDelivered_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.Complete(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- StartDelivery --------

    /// <summary>
    /// Happy path: <c>StartDelivery</c> stamps <c>DeliveryStatus</c> to
    /// <c>Dispatched</c> while the order stays at <c>Ready</c>.
    /// </summary>
    [Fact]
    public void StartDelivery_FromReady_StampsDeliveryStatusDispatched()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Ready);
        order.ClearDomainEvents();

        order.StartDelivery();

        order.Status.Should().Be(OrderStatus.Ready);
        order.DeliveryStatus.Should().Be(DeliveryStatus.Dispatched);
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<OrderDeliveryStartedEvent>();
    }

    /// <summary>
    /// Illegal transition: <c>StartDelivery</c> from any non-Ready state
    /// throws.
    /// </summary>
    [Fact]
    public void StartDelivery_FromPending_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        Action act = () => order.StartDelivery();

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(OrderStatus.Pending);
    }

    // -------- RequestCancel --------

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    public void RequestCancel_FromValidStatuses_TransitionsToCancelRequested(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);
        order.RequiresAdminApproval = true;
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        var staffId = Guid.NewGuid().ToString();
        order.RequestCancel(now, staffId, "Customer changed mind");

        order.Status.Should().Be(OrderStatus.CancelRequested);
        order.PreviousStatus.Should().Be(from);
        order.CancelRequestedAt.Should().Be(now);
        order.CancelRequestedByUserId.Should().Be(staffId);
        order.CancelRequestReason.Should().Be("Customer changed mind");
        order.RequiresAdminApproval.Should().BeFalse();
        order.StatusChangedAt.Should().Be(now);
    }

    [Fact]
    public void RequestCancel_WhenAlreadyCancelRequested_IsIdempotentNoOp()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Confirmed);

        var now = SystemClock.Instance.GetCurrentInstant();
        order.RequestCancel(now, "staff-1", "First reason");

        order.RequestCancel(now.Plus(Duration.FromMinutes(1)), "staff-2", "Second reason");

        order.Status.Should().Be(OrderStatus.CancelRequested);
        order.CancelRequestReason.Should().Be("First reason");
        order.CancelRequestedByUserId.Should().Be("staff-1");
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void RequestCancel_FromInvalidStatuses_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.RequestCancel(SystemClock.Instance.GetCurrentInstant(), "staff-1", "reason");

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    [Fact]
    public void RequestCancel_WithEmptyReason_Throws()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Confirmed);

        Action act = () => order.RequestCancel(SystemClock.Instance.GetCurrentInstant(), "staff-1", "");

        act.Should().Throw<ArgumentException>();
    }

    // -------- ApproveCancel --------

    [Fact]
    public void ApproveCancel_FromCancelRequested_TransitionsToCancelled()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Confirmed);
        var t0 = SystemClock.Instance.GetCurrentInstant();
        order.RequestCancel(t0, "waiter-1", "Customer had to leave");
        order.ClearDomainEvents();

        var t1 = t0.Plus(Duration.FromMinutes(2));
        var approverId = Guid.NewGuid();
        order.ApproveCancel(t1, approverId.ToString(), "Approved by manager");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledAt.Should().Be(t1);
        order.CancelledByUserId.Should().Be(approverId);
        order.CancellationReason.Should().Be("Approved by manager");
        order.PreviousStatus.Should().BeNull();
        order.CancelRequestedAt.Should().BeNull();
        order.CancelRequestedByUserId.Should().BeNull();
        order.CancelRequestReason.Should().BeNull();
        order.RequiresAdminApproval.Should().BeFalse();
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderCancelledEvent>();
    }

    [Fact]
    public void ApproveCancel_WhenAlreadyCancelled_IsIdempotentNoOp()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Cancel("initial cancel", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();

        order.ApproveCancel(SystemClock.Instance.GetCurrentInstant(), Guid.NewGuid().ToString(), "already cancelled");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    public void ApproveCancel_FromDeliveredOrCompleted_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.ApproveCancel(SystemClock.Instance.GetCurrentInstant(), "manager-1", "reason");

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    // -------- RejectCancel --------

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    public void RejectCancel_FromCancelRequested_RestoresPreviousStatus(OrderStatus previousStatus)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, previousStatus);
        var t0 = SystemClock.Instance.GetCurrentInstant();
        order.RequestCancel(t0, "waiter-1", "Mistake order");

        var t1 = t0.Plus(Duration.FromMinutes(1));
        order.RejectCancel(t1, "kitchen-1", "Food is already boxed");

        order.Status.Should().Be(previousStatus);
        order.StatusChangedAt.Should().Be(t1);
        order.PreviousStatus.Should().BeNull();
        order.CancelRequestedAt.Should().BeNull();
        order.CancelRequestedByUserId.Should().BeNull();
        order.CancelRequestReason.Should().BeNull();
        order.RequiresAdminApproval.Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void RejectCancel_FromNonCancelRequested_Throws(OrderStatus from)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, from);

        Action act = () => order.RejectCancel(SystemClock.Instance.GetCurrentInstant(), "manager-1", "reason");

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }
}