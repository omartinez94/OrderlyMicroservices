using BuildingBlocks.Messaging.Events.Ordering;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Logging;
using Ordering.Application.Data;
using Ordering.Application.Tests.Abstractions;
using Ordering.Infrastructure.Services;

namespace Ordering.Application.Tests.Services;

public sealed class DailyReconciliationRunnerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 24, 12, 0, 0);

    private static IClock FixedClock(Instant now)
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);
        return clock;
    }

    private static Order CreateOrder(string number, OrderStatus status, Instant createdAt)
    {
        Order order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of(number),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "4242"));

        order.Status = status;
        order.CreatedAt = createdAt;
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task RunAsync_WithStalePendingOrders_CancelsAndPublishesOutboxEvents()
    {
        IClock clock = FixedClock(Now);
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();

        Order stalePending1 = CreateOrder("ORD-001", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(20)));
        Order stalePending2 = CreateOrder("ORD-002", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(16)));
        Order recentPending = CreateOrder("ORD-003", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(10)));
        Order staleConfirmed = CreateOrder("ORD-004", OrderStatus.Confirmed, Now.Minus(Duration.FromMinutes(30)));

        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([stalePending1, stalePending2, recentPending, staleConfirmed]));

        DailyReconciliationRunner runner = new(dbContext, clock, outboxPublisher, logger);

        int reconciled = await runner.RunAsync(CancellationToken.None);

        reconciled.Should().Be(2);

        stalePending1.Status.Should().Be(OrderStatus.Cancelled);
        stalePending1.CancellationReason.Should().Be(DailyReconciliationRunner.StalePendingCancellationReason);
        stalePending1.CancelledAt.Should().Be(Now);

        stalePending2.Status.Should().Be(OrderStatus.Cancelled);
        recentPending.Status.Should().Be(OrderStatus.Pending);
        staleConfirmed.Status.Should().Be(OrderStatus.Confirmed);

        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await outboxPublisher.Received(2).PublishAsync(
            Arg.Any<OrderCancelledIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }
}
