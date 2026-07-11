using MassTransit;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderPrepStartedIntegrationEventHandler"/>. Verifies
/// the upstream <c>Order</c> transitions from <c>Confirmed</c> to
/// <c>Preparing</c> on receipt of the kitchen's "first item started" signal
/// and that a missing order is logged + skipped (the broker re-delivers).
/// </summary>
public sealed class KitchenOrderPrepStartedIntegrationEventHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateConfirmedOrder()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        // Drive Pending → Confirmed via the public guard so the legal
        // transition is reflected in the aggregate's domain-event trail too.
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Consume_ConfirmedOrder_MarksPreparingAndSaves()
    {
        var order = CreateConfirmedOrder();
        var dbContext = NewDbContextWith(order);

        var staffUserId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var startedAt = SystemClock.Instance.GetCurrentInstant();

        var consumer = new KitchenOrderPrepStartedIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderPrepStartedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderPrepStartedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderPrepStartedIntegrationEvent
        {
            OrderId = order.Id.Value,
            ItemId = itemId,
            StaffUserId = staffUserId,
            StartedAt = startedAt,
        });

        await consumer.Consume(context);

        order.Status.Should().Be(OrderStatus.Preparing);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnknownOrder_SkipsAndDoesNotSave()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var consumer = new KitchenOrderPrepStartedIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderPrepStartedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderPrepStartedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderPrepStartedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            StaffUserId = Guid.NewGuid(),
            StartedAt = SystemClock.Instance.GetCurrentInstant(),
        });

        await consumer.Consume(context);

        await dbContext.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Consume_AlreadyPreparingOrder_ThrowsAndDoesNotSave()
    {
        // A duplicate delivery (the broker can replay an event whose first
        // delivery succeeded on a different replica) must surface as a
        // domain exception so MassTransit nacks it for retry rather than
        // silently double-publishing a state transition.
        var order = CreateConfirmedOrder();
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        var dbContext = NewDbContextWith(order);

        var consumer = new KitchenOrderPrepStartedIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderPrepStartedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderPrepStartedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderPrepStartedIntegrationEvent
        {
            OrderId = order.Id.Value,
            ItemId = Guid.NewGuid(),
            StaffUserId = Guid.NewGuid(),
            StartedAt = SystemClock.Instance.GetCurrentInstant(),
        });

        Func<Task> act = () => consumer.Consume(context);

        await act.Should().ThrowAsync<InvalidOrderStateTransitionException>();
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