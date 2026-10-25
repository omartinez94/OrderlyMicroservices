using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Integration tests for Phase 1 of <c>ORDERS_LIVE_VIEW_PLAN.md</c>:
/// <list type="bullet">
/// <item><c>GET /v1/orders</c> filter, search, sort, and validation contracts;</item>
/// <item><c>GET /v1/orders/live</c> active + recently-closed set, <c>since</c> catch-up, and ISO-8601 <c>serverTime</c>;</item>
/// <item><c>GET /v1/orders/stats</c> permission gating (<c>orders:view_all</c>, <c>payments:view_reports</c>) and restaurant time-zone boundaries.</item>
/// </list>
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrdersLiveAndStatsEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public OrdersLiveAndStatsEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(Guid restaurantId, string permissions)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", permissions);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.ToString());
        return client;
    }

    private async Task<Order> SeedOrderAsync(
        Guid restaurantId,
        string orderNumber,
        OrderStatus status,
        OrderType orderType = OrderType.DineIn,
        decimal totalAmount = 25m,
        string itemName = "Tacos")
    {
        CustomerId customerId = CustomerId.Of(Guid.NewGuid());
        MenuItemId menuItemId = MenuItemId.Of(Guid.NewGuid());

        Customer customer = Customer.Create(
            customerId,
            $"test-{customerId.Value:N}@test.local",
            "Test User",
            "555-0100");

        Order order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            customerId,
            OrderNumber.Of(orderNumber),
            restaurantId,
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Address.Of("123 Main St", "Monterrey", "NL", "64000", "MX"),
            Payment.Of(PaymentMethod.Card, "Visa", "4242"));

        order.Status = status;
        order.OrderType = orderType;
        order.TotalAmount = totalAmount;
        order.Subtotal = totalAmount;
        order.Add(menuItemId, quantity: 2, price: totalAmount / 2);
        order.OrderItems.Single().MenuItemName = itemName;
        order.ClearDomainEvents();

        using IServiceScope scope = _factory.Services.CreateScope();
        IApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(MenuItem.Create(menuItemId, itemName, totalAmount / 2));
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order;
    }

    [Fact]
    public async Task GetOrders_WithStatusOrderTypeAndSearchFilters_ReturnsFilteredCountAndData()
    {
        Guid restaurantId = Guid.NewGuid();
        await SeedOrderAsync(restaurantId, "DIN20261024-0001", OrderStatus.Pending, OrderType.DineIn, itemName: "Enchiladas");
        await SeedOrderAsync(restaurantId, "TAK20261024-0002", OrderStatus.Preparing, OrderType.Takeout, itemName: "Chilaquiles");
        await SeedOrderAsync(restaurantId, "TAK20261024-0003", OrderStatus.Ready, OrderType.Takeout, itemName: "Chilaquiles");

        HttpClient client = CreateClient(restaurantId, "orders:view_own");

        HttpResponseMessage response = await client.GetAsync(
            "/v1/orders?status=Preparing&status=Ready&orderType=Takeout&search=TAK20261024");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response.Content.ReadAsStringAsync()}");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement orders = body.GetProperty("orders");
        orders.GetProperty("count").GetInt64().Should().Be(2);
        orders.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetOrders_WithInvalidStatusOrMalformedFrom_Returns400()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(restaurantId, "orders:view_own");

        HttpResponseMessage badStatus = await client.GetAsync("/v1/orders?status=NotAValidStatus");
        badStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpResponseMessage badFrom = await client.GetAsync("/v1/orders?from=not-an-instant");
        badFrom.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLiveOrders_ReturnsActiveAndRecentlyClosedWithIsoServerTime_AndSupportsSince()
    {
        Guid restaurantId = Guid.NewGuid();
        Order activeOrder = await SeedOrderAsync(restaurantId, "DIN20261024-0101", OrderStatus.Preparing);
        Order closedOrder = await SeedOrderAsync(restaurantId, "DIN20261024-0102", OrderStatus.Completed);

        HttpClient client = CreateClient(restaurantId, "orders:view_own");

        HttpResponseMessage snapshotResponse = await client.GetAsync("/v1/orders/live");
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await snapshotResponse.Content.ReadAsStringAsync()}");

        JsonElement snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        string? serverTimeStr = snapshot.GetProperty("serverTime").GetString();
        serverTimeStr.Should().NotBeNullOrWhiteSpace();
        snapshot.GetProperty("truncated").GetBoolean().Should().BeFalse();
        snapshot.GetProperty("resyncRequired").GetBoolean().Should().BeFalse();

        JsonElement liveOrders = snapshot.GetProperty("orders");
        liveOrders.GetArrayLength().Should().Be(2);

        List<Guid> returnedIds = liveOrders
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        returnedIds.Should().Contain([activeOrder.Id.Value, closedOrder.Id.Value]);

        // Incremental catch-up using the serverTime returned in the snapshot
        Order newOrder = await SeedOrderAsync(restaurantId, "DIN20261024-0103", OrderStatus.Pending);

        HttpResponseMessage sinceResponse = await client.GetAsync(
            $"/v1/orders/live?since={Uri.EscapeDataString(serverTimeStr!)}");
        sinceResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await sinceResponse.Content.ReadAsStringAsync()}");

        JsonElement sinceBody = await sinceResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement deltaOrders = sinceBody.GetProperty("orders");
        deltaOrders.EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .Should().Contain(newOrder.Id.Value);
    }

    [Fact]
    public async Task GetOrdersStats_EnforcesPermissionsAndRespectsRestaurantTimeZone()
    {
        Guid restaurantId = Guid.NewGuid();
        await SeedOrderAsync(restaurantId, "DIN20261024-0201", OrderStatus.Pending, OrderType.DineIn, totalAmount: 45m);
        await SeedOrderAsync(restaurantId, "TAK20261024-0202", OrderStatus.Completed, OrderType.Takeout, totalAmount: 55m);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            dbContext.RestaurantSettings.Add(new RestaurantSetting
            {
                RestaurantId = restaurantId,
                TimeZoneId = "America/Monterrey",
                UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
            });
            await dbContext.SaveChangesAsync();
        }

        // 1. Caller with only orders:view_own gets 403
        HttpClient ownOnlyClient = CreateClient(restaurantId, "orders:view_own");
        HttpResponseMessage forbiddenResponse = await ownOnlyClient.GetAsync("/v1/orders/stats");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Caller with orders:view_all (no payments:view_reports) gets 200 with revenueToday = null
        HttpClient managerNoReportsClient = CreateClient(restaurantId, "orders:view_all");
        HttpResponseMessage statsNoRevenueResponse = await managerNoReportsClient.GetAsync("/v1/orders/stats");
        statsNoRevenueResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await statsNoRevenueResponse.Content.ReadAsStringAsync()}");

        JsonElement statsNoRevenue = await statsNoRevenueResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        statsNoRevenue.GetProperty("timeZone").GetString().Should().Be("America/Monterrey");
        statsNoRevenue.GetProperty("timeZoneIsFallback").GetBoolean().Should().BeFalse();
        statsNoRevenue.GetProperty("activeCount").GetInt32().Should().Be(1);
        statsNoRevenue.GetProperty("ordersToday").GetInt32().Should().Be(2);
        statsNoRevenue.GetProperty("revenueToday").ValueKind.Should().Be(JsonValueKind.Null);

        // 3. Caller with orders:view_all + payments:view_reports gets 200 with revenueToday = 100
        HttpClient managerWithReportsClient = CreateClient(restaurantId, "orders:view_all,payments:view_reports");
        HttpResponseMessage statsWithRevenueResponse = await managerWithReportsClient.GetAsync("/v1/orders/stats");
        statsWithRevenueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement statsWithRevenue = await statsWithRevenueResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        statsWithRevenue.GetProperty("revenueToday").GetDecimal().Should().Be(100m);
    }
}
