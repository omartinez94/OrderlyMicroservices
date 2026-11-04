namespace Ordering.Application.Tests.Dashboard;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NodaTime;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dashboard.Dtos;
using Ordering.Application.Data;
using Ordering.Application.Tests.Abstractions;
using Ordering.Domain.Models;
using Ordering.Infrastructure.Caching;
using Ordering.Infrastructure.Services;

public class DashboardSummaryServiceTests
{
    private static readonly Guid TestRestaurantId = Guid.NewGuid();

    private static Order CreateTestOrder(
        string number,
        OrderStatus status,
        Instant createdAt,
        decimal total = 50m,
        PaymentMethod paymentMethod = PaymentMethod.Card,
        int prepMinutes = 15)
    {
        Order order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of(number),
            TestRestaurantId,
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Payment.Of(paymentMethod, "Visa", "4242"));

        order.Status = status;
        order.CreatedAt = createdAt;
        order.ConfirmedAt = createdAt;
        order.EstimatedPrepTimeMinutes = prepMinutes;
        order.TotalAmount = total;
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task GetLiveSummaryAsync_WhenNoOrdersExist_ReturnsZeroedMetricsWithoutExceptions()
    {
        Instant now = Instant.FromUtc(2026, 10, 31, 12, 0, 0);
        DateTimeZone zone = DateTimeZone.Utc;

        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([]));

        IRestaurantTimeZoneProvider tzProvider = Substitute.For<IRestaurantTimeZoneProvider>();
        tzProvider.GetTimeZoneAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new RestaurantTimeZoneResolution(zone, "UTC", IsFallback: false));

        ICatalogDashboardReader catalogReader = Substitute.For<ICatalogDashboardReader>();
        catalogReader.GetFloorPulseAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new CatalogFloorPulseResult(null, [], "USD", "ok"));

        IKitchenDashboardReader kitchenReader = Substitute.For<IKitchenDashboardReader>();
        kitchenReader.GetVelocityAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new KitchenVelocityResult(null, "ok"));

        IDashboardSnapshotCache cache = Substitute.For<IDashboardSnapshotCache>();
        cache.GetOrComputeAsync(
                TestRestaurantId,
                Arg.Any<bool>(),
                Arg.Any<Func<Task<RestaurantOperationsDashboardDto>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var factory = callInfo.Arg<Func<Task<RestaurantOperationsDashboardDto>>>();
                return factory();
            });

        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);

        DashboardSummaryService service = new(
            dbContext,
            tzProvider,
            catalogReader,
            kitchenReader,
            cache,
            clock,
            NullLogger<DashboardSummaryService>.Instance);

        RestaurantOperationsDashboardDto result = await service.GetLiveSummaryAsync(
            TestRestaurantId,
            includeRevenue: true,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Ticker.Should().NotBeNull();
        result.Ticker!.NetSalesToday.Should().Be(0m);
        result.Ticker.ActiveOrdersCount.Should().Be(0);
        result.Ticker.SalesGrowthPercentage.Should().Be(0.0);
        result.Ticker.OccupancyPercentage.Should().Be(0.0);
        result.SalesPace.Should().NotBeNull();
        result.SalesPace!.HourlySales.Should().HaveCount(24);
        result.SalesPace.OpenBalanceTotal.Should().Be(0m);
        result.OpenChecks.Should().BeEmpty();
        result.ActiveAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLiveSummaryAsync_WhenDownstreamServicesDegraded_MarksMetaSectionsDegraded()
    {
        Instant now = Instant.FromUtc(2026, 10, 31, 12, 0, 0);
        DateTimeZone zone = DateTimeZone.Utc;

        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From([]));

        IRestaurantTimeZoneProvider tzProvider = Substitute.For<IRestaurantTimeZoneProvider>();
        tzProvider.GetTimeZoneAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new RestaurantTimeZoneResolution(zone, "UTC", IsFallback: false));

        ICatalogDashboardReader catalogReader = Substitute.For<ICatalogDashboardReader>();
        catalogReader.GetFloorPulseAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new CatalogFloorPulseResult(null, [], "USD", "degraded"));

        IKitchenDashboardReader kitchenReader = Substitute.For<IKitchenDashboardReader>();
        kitchenReader.GetVelocityAsync(TestRestaurantId, Arg.Any<CancellationToken>())
            .Returns(new KitchenVelocityResult(null, "unavailable"));

        IDashboardSnapshotCache cache = Substitute.For<IDashboardSnapshotCache>();
        cache.GetOrComputeAsync(
                TestRestaurantId,
                Arg.Any<bool>(),
                Arg.Any<Func<Task<RestaurantOperationsDashboardDto>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var factory = callInfo.Arg<Func<Task<RestaurantOperationsDashboardDto>>>();
                return factory();
            });

        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);

        DashboardSummaryService service = new(
            dbContext,
            tzProvider,
            catalogReader,
            kitchenReader,
            cache,
            clock,
            NullLogger<DashboardSummaryService>.Instance);

        RestaurantOperationsDashboardDto result = await service.GetLiveSummaryAsync(
            TestRestaurantId,
            includeRevenue: true,
            CancellationToken.None);

        result.Meta.Sections["floorPulse"].Should().Be("degraded");
        result.Meta.Sections["kitchenVelocity"].Should().Be("unavailable");
        result.ActiveAlerts.Should().Contain(a => a.Category == "system_degraded");
    }
}
