namespace Ordering.Application.Tests.EventHandlers.Domain;

/// <summary>
/// When <see cref="OrderCreatedEventHandler"/> publishes, it MUST emit an
/// <see cref="OrderCreatedIntegrationEvent"/> via <see cref="IOutboxPublisher"/>
/// with no <c>Payment</c>-derived properties leaking onto the bus. This test exists because the historical
/// code path published the full <c>OrderDto</c>, which included
/// <c>PaymentDto.CardName / CardNumber / Ccv / Expiration / PaymentMethod</c>.
/// </summary>
public sealed class OrderCreatedEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    /// <summary>
    /// The published payload must be <see cref="OrderCreatedIntegrationEvent"/>,
    /// staged through <see cref="IOutboxPublisher"/>, not <c>OrderDto</c>. This is the load-bearing assertion: the bus stops
    /// carrying the rich internal DTO the moment this contract lands.
    /// </summary>
    [Fact]
    public async Task Handle_PublishesOrderCreatedIntegrationEvent()
    {
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        OrderCreatedEventHandler handler = new(outboxPublisher, NullLogger<OrderCreatedEventHandler>.Instance);

        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Add(NewMenuItemId(), quantity: 2, price: 9.99m);

        OrderCreatedEvent domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        await outboxPublisher.Received(1).PublishAsync(
            Arg.Any<OrderCreatedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
        await outboxPublisher.DidNotReceive().PublishAsync(
            Arg.Any<OrderDto>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The published event must carry no <c>Payment</c>-derived property names.
    /// This is the explicit acceptance criterion
    /// "zero PaymentDto properties appear in any RabbitMQ-bound message."
    /// We probe the public surface via reflection so any future addition that
    /// re-introduces cardholder data fails this test loudly.
    /// </summary>
    [Fact]
    public async Task Handle_PublishedEventCarriesNoPaymentProperties()
    {
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        OrderCreatedEventHandler handler = new(outboxPublisher, NullLogger<OrderCreatedEventHandler>.Instance);

        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);

        OrderCreatedEvent domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        // Capture the message that was published.
        var call = outboxPublisher.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        object publishedMessage = call.GetArguments()[0]!;
        Type publishedType = publishedMessage.GetType();

        // The hard guarantee: NO property name overlaps with PaymentDto.
        HashSet<string> paymentPropertyNames = typeof(PaymentDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> eventPropertyNames = publishedType
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        eventPropertyNames.Should().NotIntersectWith(paymentPropertyNames,
            because: "no Payment* property may leak onto the message bus");
    }

    /// <summary>
    /// The mapping must populate every kitchen-relevant field so that the
    /// downstream KitchenTicket projection can be built without further HTTP
    /// reads back into Ordering.
    /// </summary>
    [Fact]
    public async Task Handle_MapsEveryRequiredFieldFromTheAggregate()
    {
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        OrderCreatedEventHandler handler = new(outboxPublisher, NullLogger<OrderCreatedEventHandler>.Instance);

        OrderId orderId = NewOrderId();
        CustomerId customerId = NewCustomerId();
        OrderNumber orderNumber = ValidOrderNumber();
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();

        Order order = Order.Create(
            orderId, customerId, orderNumber, restaurantId,
            ValidAddress(), ValidAddress(), ValidPayment());
        order.TableId = tableId;
        order.OrderType = OrderType.DineIn;
        order.EstimatedPrepTimeMinutes = 17;

        OrderCreatedEvent domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        var call = outboxPublisher.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        OrderCreatedIntegrationEvent evt = (OrderCreatedIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(orderId.Value);
        evt.OrderNumber.Should().Be(orderNumber.Value);
        evt.RestaurantId.Should().Be(restaurantId);
        evt.CustomerId.Should().Be(customerId.Value);
        evt.OrderType.Should().Be((int)OrderType.DineIn);
        evt.TableId.Should().Be(tableId);
        evt.EstimatedPrepTimeMinutes.Should().Be(17);
        evt.BillingAddress.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that full item metadata (customizations, variations, seat number, special instructions)
    /// is preserved when publishing OrderCreatedIntegrationEvent to the outbox.
    /// </summary>
    [Fact]
    public async Task Handle_PreservesFullItemMetadataInIntegrationEvent()
    {
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        OrderCreatedEventHandler handler = new(outboxPublisher, NullLogger<OrderCreatedEventHandler>.Instance);

        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        List<KitchenOrderItemCustomization> customizations =
        [
            new("Extra sauce", "Hot", 0.50m),
        ];
        List<KitchenOrderItemVariation> variations =
        [
            new("Size: Large", 2.00m),
        ];

        order.Add(
            NewMenuItemId(),
            quantity: 2,
            price: 15.00m,
            itemName: "Gourmet Burger",
            specialInstructions: "Well done",
            customizations: customizations,
            selectedVariations: variations,
            seatNumber: 1);

        OrderCreatedEvent domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        var call = outboxPublisher.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        OrderCreatedIntegrationEvent evt = (OrderCreatedIntegrationEvent)call.GetArguments()[0]!;

        evt.Items.Should().HaveCount(1);
        KitchenOrderItemPreview item = evt.Items[0];
        item.MenuItemName.Should().Be("Gourmet Burger");
        item.SpecialInstructions.Should().Be("Well done");
        item.SeatNumber.Should().Be(1);
        item.Quantity.Should().Be(2);
        item.UnitPrice.Should().Be(15.00m);
        item.Customizations.Should().BeEquivalentTo(customizations);
        item.SelectedVariations.Should().BeEquivalentTo(variations);
    }
}
