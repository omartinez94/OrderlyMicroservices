using System.Security.Claims;
using BuildingBlocks.Multitenancy;
using MassTransit;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderBumpedIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> transitions to <c>Delivered</c> on receipt of the
/// kitchen's bump signal, handles out-of-order transitions, and is idempotent.
/// </summary>
public sealed class KitchenOrderBumpedIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateReadyOrder()
    {
        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Consume_ReadyOrder_MarksDeliveredAndSaves()
    {
        Order order = CreateReadyOrder();
        IApplicationDbContext dbContext = NewDbContextWith(order);
        ICurrentRestaurantProvider tenantProvider = Substitute.For<ICurrentRestaurantProvider>();
        ClaimsPrincipal? attachedPrincipal = null;
        tenantProvider.Attach(Arg.Do<ClaimsPrincipal>(p => attachedPrincipal = p))
            .Returns(Substitute.For<IDisposable>());

        IClock clock = Substitute.For<IClock>();
        Instant now = Instant.FromUtc(2026, 10, 24, 18, 0);
        clock.GetCurrentInstant().Returns(now);

        Guid staffUserId = Guid.NewGuid();
        KitchenOrderBumpedIntegrationEventHandler consumer = new(
            dbContext,
            clock,
            NullLogger<KitchenOrderBumpedIntegrationEventHandler>.Instance,
            tenantProvider);

        KitchenOrderBumpedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            BumpedByUserId = staffUserId,
            BumpedAt = now,
        };

        ConsumeContext<KitchenOrderBumpedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        attachedPrincipal.Should().NotBeNull();
        attachedPrincipal!.FindFirstValue("restaurantId").Should().Be(order.RestaurantId.ToString());
        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    public async Task Consume_OutOfOrder_AdvancesToDeliveredAndSaves(OrderStatus startingStatus)
    {
        Order order = CreateReadyOrder();
        typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, startingStatus);
        IApplicationDbContext dbContext = NewDbContextWith(order);

        IClock clock = Substitute.For<IClock>();
        Instant now = Instant.FromUtc(2026, 10, 24, 18, 0);
        clock.GetCurrentInstant().Returns(now);

        KitchenOrderBumpedIntegrationEventHandler consumer = new(
            dbContext,
            clock,
            NullLogger<KitchenOrderBumpedIntegrationEventHandler>.Instance);

        KitchenOrderBumpedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = now,
        };

        ConsumeContext<KitchenOrderBumpedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.CancelRequested)]
    public async Task Consume_AlreadyDeliveredOrCancelRequested_IsIdempotentNoOp(OrderStatus startingStatus)
    {
        Order order = CreateReadyOrder();
        typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, startingStatus);
        IApplicationDbContext dbContext = NewDbContextWith(order);

        IClock clock = Substitute.For<IClock>();
        KitchenOrderBumpedIntegrationEventHandler consumer = new(
            dbContext,
            clock,
            NullLogger<KitchenOrderBumpedIntegrationEventHandler>.Instance);

        KitchenOrderBumpedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = SystemClock.Instance.GetCurrentInstant(),
        };

        ConsumeContext<KitchenOrderBumpedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        order.Status.Should().Be(startingStatus);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        IClock clock = Substitute.For<IClock>();
        KitchenOrderBumpedIntegrationEventHandler consumer = new(
            dbContext,
            clock,
            NullLogger<KitchenOrderBumpedIntegrationEventHandler>.Instance);

        ConsumeContext<KitchenOrderBumpedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderBumpedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = SystemClock.Instance.GetCurrentInstant(),
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
}
