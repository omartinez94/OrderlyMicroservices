using BuildingBlocks.Enums;
using BuildingBlocks.Messaging.Events.Ordering;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Logging;
using NodaTime;
using Ordering.Application.Data;
using Ordering.Application.Tests.Abstractions;
using Ordering.Domain.Models;
using Ordering.Domain.ValueObjects;
using Ordering.Infrastructure.Services;

namespace Ordering.Infrastructure.Tests.Services;

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
    public async Task RunAsync_WithStalePendingOrders_CancelsThemAndPublishesOutboxEvents()
    {
        IClock clock = FixedClock(Now);
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();

        // Stale pending orders (>= 15 min old)
        Order stalePending1 = CreateOrder("ORD-001", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(20)));
        Order stalePending2 = CreateOrder("ORD-002", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(15)));

        // Recent pending order (< 15 min old) - should NOT be cancelled
        Order recentPending = CreateOrder("ORD-003", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(10)));

        // Non-pending order (Confirmed 30 min old) - should NOT be cancelled
        Order staleConfirmed = CreateOrder("ORD-004", OrderStatus.Confirmed, Now.Minus(Duration.FromMinutes(30)));

        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([stalePending1, stalePending2, recentPending, staleConfirmed]));

        DailyReconciliationRunner runner = new(dbContext, clock, outboxPublisher, logger);

        int reconciledCount = await runner.RunAsync(CancellationToken.None);

        reconciledCount.Should().Be(2);

        // Check entities
        stalePending1.Status.Should().Be(OrderStatus.Cancelled);
        stalePending1.CancellationReason.Should().Be(DailyReconciliationRunner.StalePendingCancellationReason);
        stalePending1.CancelledAt.Should().Be(Now);

        stalePending2.Status.Should().Be(OrderStatus.Cancelled);
        stalePending2.CancellationReason.Should().Be(DailyReconciliationRunner.StalePendingCancellationReason);

        recentPending.Status.Should().Be(OrderStatus.Pending);
        staleConfirmed.Status.Should().Be(OrderStatus.Confirmed);

        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Check Outbox publisher calls
        await outboxPublisher.Received(2).PublishAsync(
            Arg.Any<OrderCancelledIntegrationEvent>(),
            Arg.Any<CancellationToken>());

        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == stalePending1.Id.Value &&
                e.OrderNumber == "ORD-001" &&
                e.Reason == DailyReconciliationRunner.StalePendingCancellationReason &&
                e.CancelledAt == Now &&
                e.CancelledByUserId == Order.SystemUserId),
            Arg.Any<CancellationToken>());

        await outboxPublisher.Received(1).PublishAsync(
            Arg.Is<OrderCancelledIntegrationEvent>(e =>
                e.OrderId == stalePending2.Id.Value &&
                e.OrderNumber == "ORD-002" &&
                e.Reason == DailyReconciliationRunner.StalePendingCancellationReason &&
                e.CancelledAt == Now &&
                e.CancelledByUserId == Order.SystemUserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenNoStaleOrders_ReturnsZeroAndPublishesNothing()
    {
        IClock clock = FixedClock(Now);
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();

        Order recentPending = CreateOrder("ORD-001", OrderStatus.Pending, Now.Minus(Duration.FromMinutes(5)));
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([recentPending]));

        DailyReconciliationRunner runner = new(dbContext, clock, outboxPublisher, logger);

        int reconciledCount = await runner.RunAsync(CancellationToken.None);

        reconciledCount.Should().Be(0);
        await outboxPublisher.DidNotReceive().PublishAsync(
            Arg.Any<OrderCancelledIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_StillCompletesCleanly()
    {
        IClock clock = FixedClock(Now);
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ILogger<DailyReconciliationRunner> logger = Substitute.For<ILogger<DailyReconciliationRunner>>();
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();

        DailyReconciliationRunner runner = new(dbContext, clock, outboxPublisher, logger);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task<int>> act = () => runner.RunAsync(cts.Token);

        await act.Should().NotThrowAsync();
        int result = await act();
        result.Should().Be(0);
    }
}
