using MassTransit;
using Microsoft.EntityFrameworkCore;

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
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreatePendingOrder()
    {
        var order = Order.Create(
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
        var order = CreatePendingOrder();
        var dbContext = NewDbContextWith(order);

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();
        var consumer = new KitchenOrderAcceptedIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderAcceptedIntegrationEventHandler>.Instance);

        var message = new KitchenOrderAcceptedIntegrationEvent
        {
            OrderId = order.Id.Value,
            ConfirmedByUserId = staffId,
            ConfirmedAt = now
        };
        var context = Substitute.For<ConsumeContext<KitchenOrderAcceptedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

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
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var consumer = new KitchenOrderAcceptedIntegrationEventHandler(
            dbContext, NullLogger<KitchenOrderAcceptedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderAcceptedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderAcceptedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            ConfirmedByUserId = Guid.NewGuid(),
            ConfirmedAt = SystemClock.Instance.GetCurrentInstant()
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