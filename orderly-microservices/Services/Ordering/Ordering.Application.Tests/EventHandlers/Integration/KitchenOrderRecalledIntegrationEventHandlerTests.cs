using System.Security.Claims;
using BuildingBlocks.Multitenancy;
using MassTransit;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderRecalledIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> reverts from <c>Delivered</c> to <c>Ready</c> on receipt of
/// the kitchen's recall signal, clears DeliveredAt, and is idempotent.
/// </summary>
public sealed class KitchenOrderRecalledIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateDeliveredOrder()
    {
        Order order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());
        order.MarkDelivered(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Consume_DeliveredOrder_RecallsToReadyAndSaves()
    {
        Order order = CreateDeliveredOrder();
        IApplicationDbContext dbContext = NewDbContextWith(order);
        ICurrentRestaurantProvider tenantProvider = Substitute.For<ICurrentRestaurantProvider>();
        ClaimsPrincipal? attachedPrincipal = null;
        tenantProvider.Attach(Arg.Do<ClaimsPrincipal>(p => attachedPrincipal = p))
            .Returns(Substitute.For<IDisposable>());

        Guid staffUserId = Guid.NewGuid();
        Instant recalledAt = Instant.FromUtc(2026, 10, 24, 18, 30);
        KitchenOrderRecalledIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderRecalledIntegrationEventHandler>.Instance,
            tenantProvider);

        KitchenOrderRecalledIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            KitchenTicketId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            RecalledByUserId = staffUserId,
            RecalledAt = recalledAt,
        };

        ConsumeContext<KitchenOrderRecalledIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderRecalledIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        attachedPrincipal.Should().NotBeNull();
        attachedPrincipal!.FindFirstValue("restaurantId").Should().Be(order.RestaurantId.ToString());
        order.Status.Should().Be(OrderStatus.Ready);
        order.DeliveredAt.Should().BeNull();
        order.StatusChangedAt.Should().Be(recalledAt);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.CancelRequested)]
    public async Task Consume_AlreadyReadyOrCancelRequested_IsIdempotentNoOp(OrderStatus startingStatus)
    {
        Order order = CreateDeliveredOrder();
        typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, startingStatus);
        IApplicationDbContext dbContext = NewDbContextWith(order);

        KitchenOrderRecalledIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderRecalledIntegrationEventHandler>.Instance);

        KitchenOrderRecalledIntegrationEvent message = new()
        {
            OrderId = order.Id.Value,
            KitchenTicketId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            RecalledByUserId = Guid.NewGuid(),
            RecalledAt = SystemClock.Instance.GetCurrentInstant(),
        };

        ConsumeContext<KitchenOrderRecalledIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderRecalledIntegrationEvent>>();
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

        KitchenOrderRecalledIntegrationEventHandler consumer = new(
            dbContext,
            NullLogger<KitchenOrderRecalledIntegrationEventHandler>.Instance);

        ConsumeContext<KitchenOrderRecalledIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderRecalledIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderRecalledIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            KitchenTicketId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            RecalledByUserId = Guid.NewGuid(),
            RecalledAt = SystemClock.Instance.GetCurrentInstant(),
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
