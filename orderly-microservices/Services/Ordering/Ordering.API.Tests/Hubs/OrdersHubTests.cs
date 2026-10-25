using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NodaTime;
using NSubstitute;
using Ordering.API.Hubs;
using Ordering.API.Observability;

namespace Ordering.API.Tests.Hubs;

public sealed class OrdersHubTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly OrderingLiveMetrics _metrics = new();
    private readonly IGroupManager _groupManager = Substitute.For<IGroupManager>();
    private readonly HubCallerContext _context = Substitute.For<HubCallerContext>();

    public OrdersHubTests()
    {
        _context.ConnectionId.Returns("conn-orders-123");
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 10, 24, 15, 0, 0));
        _featureManager.GetFeatureNamesAsync().Returns(ToAsyncEnumerable(["OrdersLiveView"]));
        _featureManager.IsEnabledAsync("OrdersLiveView").Returns(true);
    }

    [Fact]
    public async Task OnConnectedAsync_WithoutRestaurantClaim_AbortsConnection()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("permissions", "orders:view_own")
        ], "test"));
        _context.User.Returns(principal);

        OrdersHub hub = CreateHub();

        await hub.OnConnectedAsync();

        _context.Received(1).Abort();
        await _groupManager.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenFeatureFlagDisabled_AbortsConnection()
    {
        _featureManager.IsEnabledAsync("OrdersLiveView").Returns(false);
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString()),
            new Claim("permissions", "orders:view_all")
        ], "test"));
        _context.User.Returns(principal);

        OrdersHub hub = CreateHub();

        await hub.OnConnectedAsync();

        _context.Received(1).Abort();
        await _groupManager.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task OnConnectedAsync_StaffWithViewOwnOnly_JoinsOnlyRestaurantGroup()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString()),
            new Claim("permissions", "orders:view_own")
        ], "test"));
        _context.User.Returns(principal);

        OrdersHub hub = CreateHub();

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RestaurantGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.DidNotReceive().AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.StatsGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.DidNotReceive().AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RevenueGroup(restaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_ManagerWithoutViewReports_JoinsRestaurantAndStatsGroup()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString()),
            new Claim("permissions", "orders:view_all")
        ], "test"));
        _context.User.Returns(principal);

        OrdersHub hub = CreateHub();

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RestaurantGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.Received(1).AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.StatsGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.DidNotReceive().AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RevenueGroup(restaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_ManagerWithViewReports_JoinsRestaurantAndRevenueGroup()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString()),
            new Claim("permissions", "orders:view_all"),
            new Claim("permissions", "payments:view_reports")
        ], "test"));
        _context.User.Returns(principal);

        OrdersHub hub = CreateHub();

        await hub.OnConnectedAsync();

        await _groupManager.Received(1).AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RestaurantGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.Received(1).AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.RevenueGroup(restaurantId),
            Arg.Any<CancellationToken>());
        await _groupManager.DidNotReceive().AddToGroupAsync(
            "conn-orders-123",
            OrdersHub.StatsGroup(restaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetServerTime_ReturnsCurrentClockUnixTimeMilliseconds()
    {
        OrdersHub hub = CreateHub();

        long serverTimeMs = hub.GetServerTime();

        serverTimeMs.Should().Be(Instant.FromUtc(2026, 10, 24, 15, 0, 0).ToUnixTimeMilliseconds());
    }

    private OrdersHub CreateHub() => new(
        _clock,
        _featureManager,
        _metrics,
        NullLogger<OrdersHub>.Instance)
    {
        Context = _context,
        Groups = _groupManager
    };

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (T item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
