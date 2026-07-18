namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderCancelledIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> cancels with reason + cancelling user id and that
/// a missing order is logged + skipped.
/// </summary>
public sealed class KitchenOrderCancelledIntegrationEventHandlerTests
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
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Consume_PendingOrder_CancelsAndSaves()
    {
        var order = CreatePendingOrder();
        var dbContext = NewDbContextWith(order);

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();
        var consumer = new KitchenOrderCancelledIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderCancelledIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderCancelledIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderCancelledIntegrationEvent
        {
            OrderId = order.Id.Value,
            Reason = "out of stock",
            CancelledByUserId = staffId,
            CancelledAt = now
        });

        await consumer.Consume(context);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("out of stock");
        order.CancelledByUserId.Should().Be(staffId);
        order.CancelledAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var consumer = new KitchenOrderCancelledIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderCancelledIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderCancelledIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderCancelledIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            Reason = "out of stock",
            CancelledByUserId = Guid.NewGuid(),
            CancelledAt = SystemClock.Instance.GetCurrentInstant()
        });

        await consumer.Consume(context);

        await dbContext.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private static IApplicationDbContext NewDbContextWith(Order order)
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));
        return dbContext;
    }
}