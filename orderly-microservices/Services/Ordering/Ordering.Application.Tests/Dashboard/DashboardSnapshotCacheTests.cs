namespace Ordering.Application.Tests.Dashboard;

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NodaTime;
using Ordering.Application.Dashboard.Dtos;
using Ordering.Infrastructure.Caching;

public class DashboardSnapshotCacheTests
{
    private static RestaurantOperationsDashboardDto CreateSampleSnapshot(Guid restaurantId)
    {
        Instant now = Instant.FromUtc(2026, 10, 31, 15, 0, 0);
        DashboardMetaDto meta = new(new Dictionary<string, string> { ["ticker"] = "ok" }, now);

        LiveOperationalTickerDto ticker = new(
            NetSalesToday: 1500m,
            SalesTargetToday: 2000m,
            SalesGrowthPercentage: 12.5,
            ActiveOrdersCount: 5,
            LateOrdersCount: 1,
            AtRiskOrdersCount: 2,
            OccupiedTablesCount: 10,
            TotalTablesCount: 20,
            OccupancyPercentage: 50.0,
            AvgPrepMinutesToday: 14.2,
            P90PrepMinutesRolling: 18.0);

        HourlySalesBucketDto bucket = new(
            now.InZone(DateTimeZone.Utc).ToOffsetDateTime(),
            15,
            GrossSales: 300m,
            OrderCount: 4,
            HistoricalAverageSales: 250m);

        SalesPaceDto salesPace = new(
            HourlySales: [bucket],
            OpenBalanceTotal: 450m,
            DiscountsTotal: 25m,
            DiscountPercentageOfSales: 1.5,
            CashTenderedTotal: 500m,
            DigitalTenderedTotal: 1000m);

        OpenCheckDto check = new(
            Guid.NewGuid(),
            "ORD-001",
            "T1",
            Balance: 75m,
            AgeMinutes: 20);

        return new RestaurantOperationsDashboardDto(
            ServerTime: now,
            RestaurantId: restaurantId,
            Currency: "USD",
            TimeZone: "UTC",
            BusinessDate: new LocalDate(2026, 10, 31),
            Meta: meta,
            Ticker: ticker,
            SalesPace: salesPace,
            KitchenVelocity: null,
            FloorPulse: null,
            OpenChecks: [check],
            Waitlist: [],
            ActiveAlerts: []);
    }

    [Fact]
    public void Redact_WhenIncludeRevenueFalse_RedactsAllFinancialFields()
    {
        Guid restaurantId = Guid.NewGuid();
        RestaurantOperationsDashboardDto raw = CreateSampleSnapshot(restaurantId);

        RestaurantOperationsDashboardDto redacted = DashboardSnapshotCache.Redact(raw, includeRevenue: false);

        redacted.Ticker.Should().NotBeNull();
        redacted.Ticker!.NetSalesToday.Should().BeNull();
        redacted.Ticker.SalesTargetToday.Should().BeNull();
        redacted.Ticker.SalesGrowthPercentage.Should().BeNull();

        redacted.SalesPace.Should().NotBeNull();
        redacted.SalesPace!.OpenBalanceTotal.Should().BeNull();
        redacted.SalesPace.DiscountsTotal.Should().BeNull();
        redacted.SalesPace.DiscountPercentageOfSales.Should().BeNull();
        redacted.SalesPace.CashTenderedTotal.Should().BeNull();
        redacted.SalesPace.DigitalTenderedTotal.Should().BeNull();
        redacted.SalesPace.HourlySales[0].GrossSales.Should().BeNull();
        redacted.SalesPace.HourlySales[0].HistoricalAverageSales.Should().BeNull();

        redacted.OpenChecks[0].Balance.Should().BeNull();

        // Non-financial fields remain intact
        redacted.Ticker.ActiveOrdersCount.Should().Be(5);
        redacted.Ticker.LateOrdersCount.Should().Be(1);
        redacted.Ticker.OccupancyPercentage.Should().Be(50.0);
        redacted.SalesPace.HourlySales[0].OrderCount.Should().Be(4);
        redacted.OpenChecks[0].OrderNumber.Should().Be("ORD-001");
    }

    [Fact]
    public void Redact_WhenIncludeRevenueTrue_PreservesAllFinancialFields()
    {
        Guid restaurantId = Guid.NewGuid();
        RestaurantOperationsDashboardDto raw = CreateSampleSnapshot(restaurantId);

        RestaurantOperationsDashboardDto result = DashboardSnapshotCache.Redact(raw, includeRevenue: true);

        result.Ticker!.NetSalesToday.Should().Be(1500m);
        result.Ticker.SalesTargetToday.Should().Be(2000m);
        result.Ticker.SalesGrowthPercentage.Should().Be(12.5);
        result.SalesPace!.OpenBalanceTotal.Should().Be(450m);
        result.SalesPace.HourlySales[0].GrossSales.Should().Be(300m);
        result.OpenChecks[0].Balance.Should().Be(75m);
    }

    [Fact]
    public async Task GetOrComputeAsync_UsesCache_WhenSnapshotCached()
    {
        Guid restaurantId = Guid.NewGuid();
        IDistributedCache distributedCache = Substitute.For<IDistributedCache>();

        int factoryCallCount = 0;
        DashboardSnapshotCache cache = new(distributedCache, NullLogger<DashboardSnapshotCache>.Instance);

        RestaurantOperationsDashboardDto result = await cache.GetOrComputeAsync(
            restaurantId,
            includeRevenue: true,
            () =>
            {
                factoryCallCount++;
                return Task.FromResult(CreateSampleSnapshot(restaurantId));
            });

        factoryCallCount.Should().Be(1);
        result.RestaurantId.Should().Be(restaurantId);
    }
}
