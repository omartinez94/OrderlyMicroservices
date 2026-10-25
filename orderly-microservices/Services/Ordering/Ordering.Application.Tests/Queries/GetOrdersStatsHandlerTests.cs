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
}
