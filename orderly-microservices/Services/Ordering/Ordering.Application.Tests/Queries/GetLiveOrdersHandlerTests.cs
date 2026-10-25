using Ordering.Application.Orders.Queries.GetLiveOrders;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

public sealed class GetLiveOrdersHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 24, 16, 0, 0);

    private static IClock FixedClock(Instant now)
    {
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);
        return clock;
    }

    private static Order CreateSeedOrder(
        string orderNumber,
        OrderStatus status,
        Instant? createdAt = null,
        Instant? lastModified = null,
        Instant? completedAt = null,
        Instant? cancelledAt = null)
    {
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of(orderNumber),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "4242"));

        order.Status = status;
        order.CreatedAt = createdAt ?? Now.Minus(Duration.FromMinutes(10));
        order.LastModified = lastModified ?? order.CreatedAt;
        order.CompletedAt = completedAt;
        order.CancelledAt = cancelledAt;
        order.Add(MenuItemId.Of(Guid.NewGuid()), quantity: 2, price: 12.50m);
        order.OrderItems.Single().MenuItemName = "Smash Burger";
        order.OrderItems.Single().PrepStatus = PrepStatus.Ready;
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_WithoutSince_ReturnsActiveAndRecentlyClosed_ExcludesOldClosedAndDrafts()
    {
        var activePending = CreateSeedOrder("DIN-0001", OrderStatus.Pending);
        var activePreparing = CreateSeedOrder("DIN-0002", OrderStatus.Preparing);
        var recentCompleted = CreateSeedOrder(
            "DIN-0003",
            OrderStatus.Completed,
            createdAt: Now.Minus(Duration.FromMinutes(30)),
            lastModified: Now.Minus(Duration.FromMinutes(5)),
            completedAt: Now.Minus(Duration.FromMinutes(5)));
        var staleCancelled = CreateSeedOrder(
            "DIN-0004",
            OrderStatus.Cancelled,
            createdAt: Now.Minus(Duration.FromMinutes(45)),
            lastModified: Now.Minus(Duration.FromMinutes(20)),
            cancelledAt: Now.Minus(Duration.FromMinutes(20)));
        var orderingDraft = CreateSeedOrder("DIN-0005", OrderStatus.Ordering);

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([
            activePending,
            activePreparing,
            recentCompleted,
            staleCancelled,
            orderingDraft,
        ]));

        var handler = new GetLiveOrdersHandler(dbContext, FixedClock(Now));
        var result = await handler.Handle(new GetLiveOrdersQuery(), CancellationToken.None);

        result.ServerTime.Should().Be(Now);
        result.Truncated.Should().BeFalse();
        result.ResyncRequired.Should().BeFalse();
        result.Orders.Select(o => o.OrderNumber).Should().BeEquivalentTo([
            "DIN-0001",
            "DIN-0002",
            "DIN-0003",
        ]);

        var summary = result.Orders.Single(o => o.OrderNumber == "DIN-0001");
        summary.ItemCount.Should().Be(2);
        summary.ItemsTotal.Should().Be(1);
        summary.ItemsReady.Should().Be(1);
        summary.PaymentBrand.Should().Be("Visa");
        summary.ItemsPreview.Should().ContainSingle(p => p.Name == "Smash Burger" && p.Quantity == 2);
    }

    [Fact]
    public async Task Handle_WithRecentSince_ReturnsOnlyUpdatedOrders()
    {
        Instant since = Now.Minus(Duration.FromMinutes(3));
        var untouched = CreateSeedOrder(
            "DIN-0001",
            OrderStatus.Preparing,
            createdAt: Now.Minus(Duration.FromMinutes(10)),
            lastModified: Now.Minus(Duration.FromMinutes(8)));
        var recentlyUpdated = CreateSeedOrder(
            "DIN-0002",
            OrderStatus.Ready,
            createdAt: Now.Minus(Duration.FromMinutes(12)),
            lastModified: Now.Minus(Duration.FromMinutes(1)));

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([untouched, recentlyUpdated]));

        var handler = new GetLiveOrdersHandler(dbContext, FixedClock(Now));
        var result = await handler.Handle(new GetLiveOrdersQuery(Since: since), CancellationToken.None);

        result.ResyncRequired.Should().BeFalse();
        result.Orders.Should().ContainSingle(o => o.OrderNumber == "DIN-0002");
    }

    [Fact]
    public async Task Handle_WithStaleSinceOlderThanWindow_ReturnsFullLiveSnapshotWithResyncRequired()
    {
        Instant staleSince = Now.Minus(Duration.FromMinutes(25));
        var active = CreateSeedOrder(
            "DIN-0001",
            OrderStatus.Pending,
            createdAt: Now.Minus(Duration.FromMinutes(40)),
            lastModified: Now.Minus(Duration.FromMinutes(40)));

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([active]));

        var handler = new GetLiveOrdersHandler(dbContext, FixedClock(Now));
        var result = await handler.Handle(new GetLiveOrdersQuery(Since: staleSince), CancellationToken.None);

        result.ResyncRequired.Should().BeTrue();
        result.Orders.Should().ContainSingle(o => o.OrderNumber == "DIN-0001");
    }
}
