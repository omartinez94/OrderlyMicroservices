using BuildingBlocks.Multitenancy;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Orders.Queries.GetOrdersStats;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

public sealed class GetOrdersStatsHandlerTests
{
    private static readonly Guid RestaurantId = Guid.NewGuid();

    private static IClock FixedClock(Instant now)
    {
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);
        return clock;
    }

    private static Order CreateOrder(
        string number,
        OrderStatus status,
        OrderType type,
        Instant createdAt,
        int estimatedPrepMinutes = 15,
        int actualPrepMinutes = 0,
        decimal totalAmount = 25m)
    {
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of(number),
            RestaurantId,
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Payment.Of(PaymentMethod.Card, "Visa", "1111"));

        order.Status = status;
        order.OrderType = type;
        order.CreatedAt = createdAt;
        order.ConfirmedAt = createdAt;
        order.EstimatedPrepTimeMinutes = estimatedPrepMinutes;
        order.ActualPrepTimeMinutes = actualPrepMinutes;
        order.TotalAmount = totalAmount;
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_RespectsRestaurantTimeZoneTodayBounds_AndComputesSlaAndRevenue()
    {
        // 2026-10-24T04:30:00Z is still 2026-10-23T22:30:00 in America/Monterrey (UTC-6).
        // Start of local day 2026-10-23 in America/Monterrey is 2026-10-23T06:00:00Z.
        Instant serverTime = Instant.FromUtc(2026, 10, 24, 4, 30, 0);
        DateTimeZone monterreyZone = DateTimeZoneProviders.Tzdb["America/Monterrey"];

        // Order placed at 2026-10-23T05:00:00Z (2026-10-22 23:00 local -> YESTERDAY in Monterrey!)
        var yesterdayOrder = CreateOrder(
            "DIN-YEST",
            OrderStatus.Completed,
            OrderType.DineIn,
            Instant.FromUtc(2026, 10, 23, 5, 0, 0),
            totalAmount: 100m);

        // Active order placed 13 mins ago with 15 min estimate (86.6% -> AtRisk)
        var atRiskOrder = CreateOrder(
            "DIN-RISK",
            OrderStatus.Preparing,
            OrderType.DineIn,
            serverTime.Minus(Duration.FromMinutes(13)),
            estimatedPrepMinutes: 15,
            totalAmount: 40m);

        // Active order placed 20 mins ago with 0 estimate -> uses default 15 min (133% -> Late)
        var lateOrder = CreateOrder(
            "TAK-LATE",
            OrderStatus.Confirmed,
            OrderType.Takeout,
            serverTime.Minus(Duration.FromMinutes(20)),
            estimatedPrepMinutes: 0,
            totalAmount: 30m);

        // Completed order today with 12m actual prep
        var completedToday = CreateOrder(
            "DEL-DONE",
            OrderStatus.Completed,
            OrderType.Delivery,
            serverTime.Minus(Duration.FromHours(2)),
            actualPrepMinutes: 12,
            totalAmount: 50m);

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([
            yesterdayOrder,
            atRiskOrder,
            lateOrder,
            completedToday,
        ]));

        var restaurantProvider = Substitute.For<ICurrentRestaurantProvider>();
        restaurantProvider.RestaurantId.Returns(RestaurantId);

        var tzProvider = Substitute.For<IRestaurantTimeZoneProvider>();
        tzProvider.GetTimeZoneAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(new RestaurantTimeZoneResolution(monterreyZone, "America/Monterrey", IsFallback: false));

        var handler = new GetOrdersStatsHandler(
            dbContext,
            restaurantProvider,
            tzProvider,
            FixedClock(serverTime));

        var withRevenue = await handler.Handle(
            new GetOrdersStatsQuery(IncludeRevenue: true),
            CancellationToken.None);

        withRevenue.Stats.TimeZone.Should().Be("America/Monterrey");
        withRevenue.Stats.TimeZoneIsFallback.Should().BeFalse();
        withRevenue.Stats.ActiveCount.Should().Be(2);
        withRevenue.Stats.AtRiskCount.Should().Be(1);
        withRevenue.Stats.LateCount.Should().Be(1);
        withRevenue.Stats.OrdersToday.Should().Be(3); // excludes yesterdayOrder!
        withRevenue.Stats.RevenueToday.Should().Be(120m); // 40 + 30 + 50
        withRevenue.Stats.AvgPrepMinutesToday.Should().Be(12.0);
        withRevenue.Stats.OldestActiveCreatedAt.Should().Be(serverTime.Minus(Duration.FromMinutes(20)));

        var withoutRevenue = await handler.Handle(
            new GetOrdersStatsQuery(IncludeRevenue: false),
            CancellationToken.None);

        withoutRevenue.Stats.RevenueToday.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EvaluatesPerPhaseSla_ForAllPhases()
    {
        Instant serverTime = Instant.FromUtc(2026, 10, 24, 12, 0, 0);
        DateTimeZone zone = DateTimeZone.Utc;

        // 1. CancelRequested: immediately late
        var cancelReq = CreateOrder("CR-1", OrderStatus.CancelRequested, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(5)));
        cancelReq.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(5));

        // 2. Pending: late at >= 120s, at-risk at >= 90s, on-track < 90s
        var pendingLate = CreateOrder("P-LATE", OrderStatus.Pending, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(130)));
        pendingLate.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(130));

        var pendingRisk = CreateOrder("P-RISK", OrderStatus.Pending, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(95)));
        pendingRisk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(95));

        var pendingOk = CreateOrder("P-OK", OrderStatus.Pending, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(60)));
        pendingOk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(60));

        // 3. Confirmed: late at >= 300s, at-risk at >= 225s, on-track < 225s
        var confLate = CreateOrder("C-LATE", OrderStatus.Confirmed, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(310)));
        confLate.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(310));

        var confRisk = CreateOrder("C-RISK", OrderStatus.Confirmed, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(240)));
        confRisk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(240));

        var confOk = CreateOrder("C-OK", OrderStatus.Confirmed, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(100)));
        confOk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(100));

        // 4. Preparing: target = 10m = 600s. Late at >= 600s, at-risk at >= 450s (75%), on-track < 450s
        var prepLate = CreateOrder("PR-LATE", OrderStatus.Preparing, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(610)), estimatedPrepMinutes: 10);
        prepLate.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(610));

        var prepRisk = CreateOrder("PR-RISK", OrderStatus.Preparing, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(460)), estimatedPrepMinutes: 10);
        prepRisk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(460));

        var prepOk = CreateOrder("PR-OK", OrderStatus.Preparing, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(300)), estimatedPrepMinutes: 10);
        prepOk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(300));

        // 5. Ready: late at >= 300s, at-risk at >= 225s, on-track < 225s
        var readyLate = CreateOrder("R-LATE", OrderStatus.Ready, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(320)));
        readyLate.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(320));

        var readyRisk = CreateOrder("R-RISK", OrderStatus.Ready, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(230)));
        readyRisk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(230));

        var readyOk = CreateOrder("R-OK", OrderStatus.Ready, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(50)));
        readyOk.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(50));

        // 6. OnHold: no SLA penalty
        var onHold = CreateOrder("HOLD", OrderStatus.OnHold, OrderType.DineIn, serverTime.Minus(Duration.FromSeconds(1000)));
        onHold.StatusChangedAt = serverTime.Minus(Duration.FromSeconds(1000));

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([
            cancelReq,
            pendingLate, pendingRisk, pendingOk,
            confLate, confRisk, confOk,
            prepLate, prepRisk, prepOk,
            readyLate, readyRisk, readyOk,
            onHold,
        ]));

        var restaurantProvider = Substitute.For<ICurrentRestaurantProvider>();
        restaurantProvider.RestaurantId.Returns(RestaurantId);

        var tzProvider = Substitute.For<IRestaurantTimeZoneProvider>();
        tzProvider.GetTimeZoneAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(new RestaurantTimeZoneResolution(zone, "UTC", IsFallback: false));

        var handler = new GetOrdersStatsHandler(
            dbContext,
            restaurantProvider,
            tzProvider,
            FixedClock(serverTime));

        var result = await handler.Handle(new GetOrdersStatsQuery(), CancellationToken.None);

        // Expected Late: cancelReq (1) + pendingLate (1) + confLate (1) + prepLate (1) + readyLate (1) = 5
        result.Stats.LateCount.Should().Be(5);

        // Expected AtRisk: pendingRisk (1) + confRisk (1) + prepRisk (1) + readyRisk (1) = 4
        result.Stats.AtRiskCount.Should().Be(4);

        // Total Active: 14
        result.Stats.ActiveCount.Should().Be(14);

        // CountsByStatus includes CancelRequested: 1
        result.Stats.CountsByStatus[nameof(OrderStatus.CancelRequested)].Should().Be(1);
        result.Stats.CountsByStatus[nameof(OrderStatus.Pending)].Should().Be(3);
        result.Stats.CountsByStatus[nameof(OrderStatus.Confirmed)].Should().Be(3);
        result.Stats.CountsByStatus[nameof(OrderStatus.Preparing)].Should().Be(3);
        result.Stats.CountsByStatus[nameof(OrderStatus.Ready)].Should().Be(3);
        result.Stats.CountsByStatus[nameof(OrderStatus.OnHold)].Should().Be(1);

        // Oldest active start is pendingLate (or onHold)
        result.Stats.OldestActiveCreatedAt.Should().Be(onHold.ConfirmedAt);
    }
}
