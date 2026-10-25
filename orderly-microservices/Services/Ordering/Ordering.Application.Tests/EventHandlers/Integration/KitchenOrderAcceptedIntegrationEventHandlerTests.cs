using System.Security.Claims;
using BuildingBlocks.Multitenancy;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderAcceptedIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> transitions from <c>Pending</c> to <c>Confirmed</c> on
/// receipt of the kitchen's accept signal and that a missing order is logged
/// + skipped (broker will redeliver if the consumer nacks).
/// </summary>
public sealed class KitchenOrderAcceptedIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreatePendingOrder()
    {
        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        // Clear the OrderCreatedEvent so the consumer's transition test is
        // not polluted by side-effects from the factory method.
        order.ClearDomainEvents();
        return order;
    }

    /// <summary>
    /// Happy path: the consumer fetches the aggregate, calls
    /// <c>Order.Confirm</c>, and persists. Asserts the order moved to
    /// <c>Confirmed</c> and the audit fields are stamped.
    /// </summary>
    [Fact]
    public async Task Consume_PendingOrder_ConfirmsAndSaves()
    {
        Order order = CreatePendingOrder();
        IApplicationDbContext dbContext = NewDbContextWith(order);
        ICurrentRestaurantProvider tenantProvider = Substitute.For<ICurrentRestaurantProvider>();
        ClaimsPrincipal? attachedPrincipal = null;
        tenantProvider.Attach(Arg.Do<ClaimsPrincipal>(p => attachedPrincipal = p))
            .Returns(Substitute.For<IDisposable>());

        Guid staffId = Guid.NewGuid();
        Instant now = SystemClock.Instance.GetCurrentInstant();
        KitchenOrderAcceptedIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderAcceptedIntegrationEventHandler>.Instance,
            tenantProvider);

        KitchenOrderAcceptedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            ConfirmedByUserId = staffId,
            ConfirmedAt = now
        };
        ConsumeContext<KitchenOrderAcceptedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderAcceptedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        attachedPrincipal.Should().NotBeNull();
        attachedPrincipal!.FindFirstValue("restaurantId").Should().Be(order.RestaurantId.ToString());
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.ConfirmedByUserId.Should().Be(staffId);
        order.ConfirmedAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Not-found: a kitchen accept arriving for an unknown order id is logged
    /// + skipped (SaveChangesAsync NOT called). The broker redelivers until
    /// Ordering's projection is in sync.
    /// </summary>
    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        KitchenOrderAcceptedIntegrationEventHandler consumer = new(
            dbContext, NullLogger<KitchenOrderAcceptedIntegrationEventHandler>.Instance);

        ConsumeContext<KitchenOrderAcceptedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderAcceptedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderAcceptedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            ConfirmedByUserId = Guid.NewGuid(),
            ConfirmedAt = SystemClock.Instance.GetCurrentInstant()
        });

        await consumer.Consume(context);

        await dbContext.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private static IApplicationDbContext NewDbContextWith(Order order)
    {
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));
        return dbContext;
    }

    /// <summary>
    /// The consumer stamps the activity row's <c>CorrelationId</c> from the
    /// bus <c>ConsumeContext.CorrelationId</c> so log-trace correlation
    /// works end-to-end. Locks in the v1 contract that the ambient id
    /// survives into the aggregate transition.
    /// </summary>
    [Fact]
    public async Task Consume_StampsCorrelationIdFromBusContext_OnActivityRow()
    {
        Order order = CreatePendingOrder();
        IApplicationDbContext dbContext = NewDbContextWith(order);

        KitchenOrderAcceptedIntegrationEventHandler consumer = new(
            dbContext, NullLogger<KitchenOrderAcceptedIntegrationEventHandler>.Instance);

        Guid correlationGuid = Guid.NewGuid();
        KitchenOrderAcceptedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            ConfirmedByUserId = Guid.NewGuid(),
            ConfirmedAt = SystemClock.Instance.GetCurrentInstant(),
        };
        ConsumeContext<KitchenOrderAcceptedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderAcceptedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CorrelationId.Returns(correlationGuid);

        await consumer.Consume(context);

        order.Activities.Single().CorrelationId.Should().Be(correlationGuid.ToString());
    }
}
