namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderReadyIntegrationEventHandler"/>. Verifies the
/// upstream <c>Order</c> transitions to <c>Ready</c> on receipt of the
/// kitchen's ready signal and that a missing order is logged + skipped.
/// </summary>
public sealed class KitchenOrderReadyIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreatePreparingOrder()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        // Seed Preparing state — the kitchen-side consumer for "started
        // first item" is not yet implemented, so tests seed via reflection.
        typeof(Order)
            .GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Preparing);
        return order;
    }

    [Fact]
    public async Task Consume_PreparingOrder_MarksReadyAndSaves()
    {
        var order = CreatePreparingOrder();
        var dbContext = NewDbContextWith(order);

        var readyAt = SystemClock.Instance.GetCurrentInstant();
        var consumer = new KitchenOrderReadyIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderReadyIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderReadyIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderReadyIntegrationEvent
        {
            OrderId = order.Id.Value,
            ReadyAt = readyAt
        });

        await consumer.Consume(context);

        order.Status.Should().Be(OrderStatus.Ready);
        order.ReadyAt.Should().Be(readyAt);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var consumer = new KitchenOrderReadyIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderReadyIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderReadyIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderReadyIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            ReadyAt = SystemClock.Instance.GetCurrentInstant()
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