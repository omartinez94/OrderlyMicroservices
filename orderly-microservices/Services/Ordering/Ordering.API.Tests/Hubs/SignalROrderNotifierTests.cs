using BuildingBlocks.Enums;
using BuildingBlocks.Multitenancy;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using Ordering.API.Hubs;
using Ordering.API.Observability;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dtos;
using Ordering.Application.Orders.Queries.GetOrdersStats;
using Ordering.Domain.Enums;

namespace Ordering.API.Tests.Hubs;

public sealed class SignalROrderNotifierTests
{
    private static readonly Guid RestaurantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 24, 15, 0, 0);

    private readonly IHubContext<OrdersHub, IOrdersHubClient> _hubContext =
        Substitute.For<IHubContext<OrdersHub, IOrdersHubClient>>();
    private readonly IHubClients<IOrdersHubClient> _hubClients =
        Substitute.For<IHubClients<IOrdersHubClient>>();
    private readonly IOrdersHubClient _restaurantGroupClient =
        Substitute.For<IOrdersHubClient>();
    private readonly IOrdersHubClient _statsGroupClient =
        Substitute.For<IOrdersHubClient>();
    private readonly IOrdersHubClient _revenueGroupClient =
        Substitute.For<IOrdersHubClient>();
    private readonly IServiceScopeFactory _scopeFactory =
        Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope =
        Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider =
        Substitute.For<IServiceProvider>();
    private readonly ICurrentRestaurantProvider _tenantProvider =
        Substitute.For<ICurrentRestaurantProvider>();
    private readonly ISender _sender =
        Substitute.For<ISender>();
    private readonly IClock _clock =
        Substitute.For<IClock>();
    private readonly OrderingLiveMetrics _metrics = new();

    public SignalROrderNotifierTests()
    {
        _clock.GetCurrentInstant().Returns(Now);
        _hubContext.Clients.Returns(_hubClients);
        _hubClients.Group(OrdersHub.RestaurantGroup(RestaurantA)).Returns(_restaurantGroupClient);
        _hubClients.Group(OrdersHub.StatsGroup(RestaurantA)).Returns(_statsGroupClient);
        _hubClients.Group(OrdersHub.RevenueGroup(RestaurantA)).Returns(_revenueGroupClient);

        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(ICurrentRestaurantProvider)).Returns(_tenantProvider);
        _serviceProvider.GetService(typeof(ISender)).Returns(_sender);
    }

    [Fact]
    public async Task NotifyUpsertedAsync_BroadcastsOrderToRestaurantGroup_AndSplitsStatsByRevenuePermission()
    {
        OrdersStatsDto fullStats = new(
            ServerTime: Now,
            TimeZone: "UTC",
            TimeZoneIsFallback: true,
            CountsByStatus: new Dictionary<string, int> { ["Pending"] = 1, ["Preparing"] = 2 },
            CountsByType: new Dictionary<string, int> { ["DineIn"] = 3 },
            ActiveCount: 3,
            LateCount: 1,
            AtRiskCount: 0,
            AvgPrepMinutesToday: 7.0,
            OldestActiveCreatedAt: Now,
            OrdersToday: 12,
            RevenueToday: 480.50m);

        _sender
            .Send(Arg.Is<GetOrdersStatsQuery>(q => q.IncludeRevenue), Arg.Any<CancellationToken>())
            .Returns(new GetOrdersStatsResult(fullStats));

        OrderSummaryDto summary = CreateOrderSummary(RestaurantA);
        OrderChangeNotification notification = new(summary, "StatusChanged", Now);

        SignalROrderNotifier notifier = CreateNotifier();

        await notifier.NotifyUpsertedAsync([notification]);

        await _restaurantGroupClient.Received(1).OrderUpserted(
            summary,
            "StatusChanged",
            Now);

        await _statsGroupClient.Received(1).StatsChanged(
            Arg.Is<OrdersStatsDto>(s => s.OrdersToday == 12 && s.RevenueToday == null));

        await _revenueGroupClient.Received(1).StatsChanged(
            Arg.Is<OrdersStatsDto>(s => s.OrdersToday == 12 && s.RevenueToday == 480.50m));
    }

    [Fact]
    public async Task NotifyRemovedAsync_BroadcastsOrderRemovedAndRefreshesStats()
    {
        OrdersStatsDto fullStats = new(
            ServerTime: Now,
            TimeZone: "UTC",
            TimeZoneIsFallback: true,
            CountsByStatus: new Dictionary<string, int>(),
            CountsByType: new Dictionary<string, int>(),
            ActiveCount: 0,
            LateCount: 0,
            AtRiskCount: 0,
            AvgPrepMinutesToday: null,
            OldestActiveCreatedAt: null,
            OrdersToday: 1,
            RevenueToday: 25.00m);

        _sender
            .Send(Arg.Any<GetOrdersStatsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GetOrdersStatsResult(fullStats));

        Guid orderId = Guid.NewGuid();
        OrderRemovalNotification removal = new(RestaurantA, orderId, Now);

        SignalROrderNotifier notifier = CreateNotifier();

        await notifier.NotifyRemovedAsync([removal]);

        await _restaurantGroupClient.Received(1).OrderRemoved(orderId);
        await _statsGroupClient.Received(1).StatsChanged(
            Arg.Is<OrdersStatsDto>(s => s.RevenueToday == null));
        await _revenueGroupClient.Received(1).StatsChanged(
            Arg.Is<OrdersStatsDto>(s => s.RevenueToday == 25.00m));
    }

    private SignalROrderNotifier CreateNotifier() => new(
        _hubContext,
        _scopeFactory,
        _clock,
        _metrics,
        NullLogger<SignalROrderNotifier>.Instance);

    private static OrderSummaryDto CreateOrderSummary(Guid restaurantId) => new(
        Id: Guid.NewGuid(),
        ShortId: "ORD-TEST01",
        OrderNumber: "DIN20261024-0001",
        RestaurantId: restaurantId,
        CustomerId: Guid.NewGuid(),
        Status: OrderStatus.Confirmed,
        OrderType: OrderType.DineIn,
        DeliveryStatus: null,
        CancellationReason: null,
        TableId: Guid.NewGuid(),
        DeliveryStreet: null,
        CreatedAt: Now,
        ConfirmedAt: Now,
        PreparingStartedAt: null,
        ReadyAt: null,
        DeliveredAt: null,
        CompletedAt: null,
        CancelledAt: null,
        LastModifiedAt: Now,
        StatusChangedAt: Now,
        EstimatedPrepTimeMinutes: 15,
        ActualPrepTimeMinutes: 0,
        Version: Now,
        Currency: "USD",
        Subtotal: 25.00m,
        TaxAmount: 0m,
        DiscountAmount: 0m,
        TotalAmount: 25.00m,
        PaymentMethod: PaymentMethod.Card,
        PaymentBrand: "Visa",
        PaymentLastFour: "4242",
        IsModified: false,
        RequiresAdminApproval: false,
        HasNotes: false,
        HasSpecialInstructions: false,
        Notes: null,
        ItemCount: 2,
        ItemsReady: 0,
        ItemsPreparing: 0,
        ItemsPending: 2,
        ItemsTotal: 2,
        ItemsPreview: [],
        CreatedByUserId: null,
        ConfirmedByUserId: null);
}
