using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage of the Ordering.API surface that does NOT require
/// a real Identity authority. Locks in:
/// <list type="bullet">
/// <item>the unauthenticated 401 path on every guarded endpoint,</item>
/// <item>the 403 path when the caller has a JWT but lacks
/// <c>kitchen:update_prep_status</c>,</item>
/// <item>the 404 path when a Guid points at no order,</item>
/// <item>the 400 path on the cancel command's empty reason,</item>
/// <item>the 204 happy path on a state-transition endpoint when a known
/// <c>Order</c> aggregate is seeded in the database,</item>
/// <item>the 200 path on the existing <c>GET /orders/...</c> endpoints.</item>
/// </list>
/// Health endpoint and broker reachability are exercised in
/// <see cref="OrderingHealthEndpointTests"/>.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingApiIntegrationTests
{
    private const string KitchenPermissions =
        "kitchen:view_orders,kitchen:update_prep_status";

    private readonly OrderingWebApplicationFactory _factory;

    public OrderingApiIntegrationTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewKitchenStaffClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", KitchenPermissions);
        return client;
    }

    private HttpClient NewReadOnlyClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");
        return client;
    }

    private async Task<Order> SeedOrderAsync(OrderStatus status, PrepStatus itemStatus = PrepStatus.Pending)
    {
        var customerId = CustomerId.Of(Guid.NewGuid());
        var customer = Customer.Create(customerId, $"test-{Guid.NewGuid():N}@test.com", "Test User", "555-1234");

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            customerId,
            OrderNumber.Of($"ORD-{Guid.NewGuid():N}"[..16]),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111"));

        // Force the desired status for the test (Create sets Pending). The
        // Aggregate exposes Status as a settable property for test seeder
        // scenarios; production callers always go through Confirm/MarkReady etc.
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, status);

        var menuItemId = MenuItemId.Of(Guid.NewGuid());
        var menuItem = MenuItem.Create(menuItemId, "Burger", 5m);

        order.Add(menuItemId, quantity: 1, price: 5m);
        order.OrderItems.Single().PrepStatus = itemStatus;

        // Strip any domain events created during construction so the
        // SaveChanges below is a pure insert (no event dispatch).
        order.ClearDomainEvents();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(menuItem);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order;
    }

    // ----- 401 anonymous paths -----

    [Fact]
    public async Task ConfirmOrder_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/confirm",
            content: null);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"body: {body}");
    }

    [Fact]
    public async Task StartOrderPrep_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MarkOrderReady_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelOrder_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/cancel",
            new { reason = "out of stock" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartItemPrep_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MarkItemReady_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MarkOrderDelivered_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/mark-delivered",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ----- 403 missing-permission path -----

    [Fact]
    public async Task ConfirmOrder_AuthenticatedButMissingPermission_Returns403()
    {
        var client = NewReadOnlyClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/confirm",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- 404 unknown-id paths -----

    [Fact]
    public async Task ConfirmOrder_UnknownId_Returns404()
    {
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/confirm",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StartOrderPrep_UnknownId_Returns404()
    {
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkOrderReady_UnknownId_Returns404()
    {
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelOrder_UnknownId_Returns404()
    {
        var client = NewKitchenStaffClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/cancel",
            new { reason = "no-show" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkOrderDelivered_UnknownId_Returns404()
    {
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/mark-delivered",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StartItemPrep_UnknownItem_Returns404()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Confirmed);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{seeded.Id.Value}/items/{Guid.NewGuid()}/start-prep",
            content: null);

        // Aggregate's per-item existence check throws
        // OrderItemNotFoundException; the global exception handler maps
        // it to 404 (not-found, not invalid state).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkItemReady_UnknownItem_Returns404()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Confirmed, PrepStatus.Preparing);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{seeded.Id.Value}/items/{Guid.NewGuid()}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----- 400 validation path (cancel reason) -----

    [Fact]
    public async Task CancelOrder_EmptyReason_Returns400()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Pending);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{seeded.Id.Value}/cancel",
            new { reason = "" });

        // FluentValidation rejects empty reason before the handler runs.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ----- 200/204 happy paths -----

    [Fact]
    public async Task ConfirmOrder_FromPending_Returns204()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Pending);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{seeded.Id.Value}/confirm",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CancelOrder_WithReason_Returns204()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Pending);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{seeded.Id.Value}/cancel",
            new { reason = "customer requested" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StartOrderPrep_FromConfirmed_Returns204()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Confirmed);
        var client = NewKitchenStaffClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{seeded.Id.Value}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetOrderById_SeededOrder_Returns200()
    {
        var seeded = await SeedOrderAsync(OrderStatus.Pending);
        var client = NewReadOnlyClient();

        var response = await client.GetAsync($"/api/v1/orders/{seeded.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrderById_UnknownId_Returns404()
    {
        var client = NewReadOnlyClient();

        var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrders_Returns200()
    {
        // Read endpoint with no body and no required aggregate state — the
        // simplest 200 smoke test on the Ordering surface.
        var client = NewReadOnlyClient();

        var response = await client.GetAsync("/api/v1/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
