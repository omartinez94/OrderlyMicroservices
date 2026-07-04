using Microsoft.FeatureManagement;

namespace Ordering.Application.Tests.EventHandlers.Domain;

/// <summary>
/// When <see cref="OrderCreatedEventHandler"/> publishes, it MUST emit an
/// <see cref="OrderCreatedIntegrationEvent"/> with no <c>Payment</c>-derived
/// properties leaking onto the bus. This test exists because the historical
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
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    /// <summary>
    /// The published payload must be <see cref="OrderCreatedIntegrationEvent"/>,
    /// not <c>OrderDto</c>. This is the load-bearing assertion: the bus stops
    /// carrying the rich internal DTO the moment this contract lands.
    /// </summary>
    [Fact]
    public async Task Handle_PublishesOrderCreatedIntegrationEvent()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("OrderFullfilment").Returns(true);

        var handler = new OrderCreatedEventHandler(publishEndpoint, featureManager, NullLogger<OrderCreatedEventHandler>.Instance);

        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Add(NewMenuItemId(), quantity: 2, price: 9.99m);

        var domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        await publishEndpoint.Received(1).Publish(
            Arg.Any<OrderCreatedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
        await publishEndpoint.DidNotReceive().Publish(
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
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("OrderFullfilment").Returns(true);

        var handler = new OrderCreatedEventHandler(publishEndpoint, featureManager, NullLogger<OrderCreatedEventHandler>.Instance);

        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);

        var domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        // Capture the message that was published.
        var call = publishEndpoint.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish));
        var publishedMessage = call.GetArguments()[0]!;
        var publishedType = publishedMessage.GetType();

        // The hard guarantee: NO property name overlaps with PaymentDto.
        var paymentPropertyNames = typeof(PaymentDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var eventPropertyNames = publishedType
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        eventPropertyNames.Should().NotIntersectWith(paymentPropertyNames,
            because: "no Payment* property may leak onto the message bus");
    }

    /// <summary>
    /// When the <c>OrderFullfilment</c> feature flag is disabled, the handler
    /// must remain a no-op — preserving the kill.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFeatureFlagDisabled_DoesNotPublish()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("OrderFullfilment").Returns(false);

        var handler = new OrderCreatedEventHandler(publishEndpoint, featureManager, NullLogger<OrderCreatedEventHandler>.Instance);

        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());

        var domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        await publishEndpoint.DidNotReceiveWithAnyArgs().Publish(default(object)!, default);
    }

    /// <summary>
    /// The mapping must populate every kitchen-relevant field so that the
    /// downstream KitchenTicket projection can be built without further HTTP
    /// reads back into Ordering.
    /// </summary>
    [Fact]
    public async Task Handle_MapsEveryRequiredFieldFromTheAggregate()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("OrderFullfilment").Returns(true);

        var handler = new OrderCreatedEventHandler(publishEndpoint, featureManager, NullLogger<OrderCreatedEventHandler>.Instance);

        var orderId = NewOrderId();
        var customerId = NewCustomerId();
        var orderNumber = ValidOrderNumber();
        var restaurantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        var order = Order.Create(
            orderId, customerId, orderNumber, restaurantId,
            ValidAddress(), ValidAddress(), ValidPayment());
        order.TableId = tableId;
        order.OrderType = OrderType.DineIn;
        order.EstimatedPrepTimeMinutes = 17;

        var domainEvent = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        await handler.Handle(domainEvent, CancellationToken.None);

        var call = publishEndpoint.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish));
        var evt = (OrderCreatedIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(orderId.Value);
        evt.OrderNumber.Should().Be(orderNumber.Value);
        evt.RestaurantId.Should().Be(restaurantId);
        evt.CustomerId.Should().Be(customerId.Value);
        evt.OrderType.Should().Be((int)OrderType.DineIn);
        evt.TableId.Should().Be(tableId);
        evt.EstimatedPrepTimeMinutes.Should().Be(17);
        evt.BillingAddress.Should().NotBeNull();
    }
}