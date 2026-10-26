using System.Security.Claims;
using BuildingBlocks.Multitenancy;
using MassTransit;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderCancelRejectedIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> in <c>CancelRequested</c> restores to its previous status when
/// kitchen staff reject cancellation on the KDS.
/// </summary>
public sealed class KitchenOrderCancelRejectedIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateCancelRequestedOrder(OrderStatus previousStatus = OrderStatus.Preparing)
    {
        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        Instant now = SystemClock.Instance.GetCurrentInstant();
        order.Confirm(Guid.NewGuid(), now);
        if (previousStatus is OrderStatus.Preparing or OrderStatus.Ready)
        {
            order.MarkPreparing(now);
        }
        if (previousStatus == OrderStatus.Ready)
        {
            order.MarkReady(now);
        }

        order.RequestCancel("Customer changed mind", Guid.NewGuid(), now);
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Consume_CancelRequestedOrder_RestoresPreviousStatusAndSaves()
    {
        Order order = CreateCancelRequestedOrder(OrderStatus.Preparing);
        order.Status.Should().Be(OrderStatus.CancelRequested);
        order.PreviousStatus.Should().Be(OrderStatus.Preparing);

        IApplicationDbContext dbContext = NewDbContextWith(order);
        ICurrentRestaurantProvider tenantProvider = Substitute.For<ICurrentRestaurantProvider>();
        ClaimsPrincipal? attachedPrincipal = null;
        tenantProvider.Attach(Arg.Do<ClaimsPrincipal>(p => attachedPrincipal = p))
            .Returns(Substitute.For<IDisposable>());

        Guid staffUserId = Guid.NewGuid();
        Instant rejectedAt = Instant.FromUtc(2026, 10, 24, 18, 30);
        KitchenOrderCancelRejectedIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderCancelRejectedIntegrationEventHandler>.Instance,
            tenantProvider);

        KitchenOrderCancelRejectedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            RejectedAt = rejectedAt,
            RejectedByUserId = staffUserId.ToString(),
            Reason = "Food is already in the fryer"
        };

        ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        attachedPrincipal.Should().NotBeNull();
        attachedPrincipal!.FindFirstValue("restaurantId").Should().Be(order.RestaurantId.ToString());
        order.Status.Should().Be(OrderStatus.Preparing);
        order.PreviousStatus.Should().BeNull();
        order.CancelRequestedAt.Should().BeNull();
        order.CancelRequestReason.Should().BeNull();
        order.StatusChangedAt.Should().Be(rejectedAt);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Consume_OrderNotInCancelRequested_IsIdempotentNoOp(OrderStatus currentStatus)
    {
        Order order = CreateCancelRequestedOrder();
        typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, currentStatus);

        IApplicationDbContext dbContext = NewDbContextWith(order);
        KitchenOrderCancelRejectedIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderCancelRejectedIntegrationEventHandler>.Instance);

        KitchenOrderCancelRejectedIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            RejectedAt = SystemClock.Instance.GetCurrentInstant(),
            RejectedByUserId = Guid.NewGuid().ToString(),
            Reason = "Denied"
        };

        ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        order.Status.Should().Be(currentStatus);
        await dbContext.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        KitchenOrderCancelRejectedIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderCancelRejectedIntegrationEventHandler>.Instance);

        ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderCancelRejectedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            OrderNumber = "ORD-2026-9999",
            RejectedAt = SystemClock.Instance.GetCurrentInstant(),
            RejectedByUserId = Guid.NewGuid().ToString(),
            Reason = "Unknown"
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
