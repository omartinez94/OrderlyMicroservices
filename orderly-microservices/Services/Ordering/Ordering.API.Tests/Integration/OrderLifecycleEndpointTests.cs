using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage of the order's full lifecycle through the public
/// HTTP endpoints:
/// <c>Pending → Confirmed → Preparing → Ready → Delivered</c>.
/// Each test wires a freshly-seeded order against the
/// <see cref="OrderingWebApplicationFactory"/>'s WAF and walks the
/// aggregate through the relevant slice of the state machine.
/// </summary>
/// <remarks>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3:
/// <list type="bullet">
/// <item>full lifecycle: Pending → Delivered, one assertion per step;</item>
/// <item>delivery: Ready → Delivered as a single transition;</item>
/// <item>cancel mid-lifecycle: Confirmed → Cancelled after a reason is
/// supplied.</item>
/// </list>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderLifecycleEndpointTests
{
    private const string KitchenPermissions = "kitchen:update_prep_status";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public OrderLifecycleEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewKitchenClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", KitchenPermissions);
        return client;
    }

    /// <summary>
    /// Seeds a pending Order with one item and returns its id. The
    /// factory's WAF uses Testcontainers MSSQL — the schema is built by
    /// the production migration runner on host start, so these inserts
    /// land on the live schema.
    /// </summary>
    private async Task<Guid> SeedPendingOrderAsync()
    {
        var customerId = CustomerId.Of(Guid.NewGuid());
        var menuItemId = MenuItemId.Of(Guid.NewGuid());

        var customer = Customer.Create(
            customerId,
            $"test-customer-{customerId.Value:N}@test.local",
            "Test Customer",
            "555-0100");

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            customerId,
            OrderNumber.Of($"ORD-{Guid.NewGuid():N}"[..16]),
            TestAuthHandler.DefaultRestaurantId,
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "1111"));

        order.Add(menuItemId, quantity: 1, price: 9.99m);
        order.ClearDomainEvents();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(MenuItem.Create(menuItemId, "Burger", 9.99m));
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order.Id.Value;
    }

    /// <summary>
    /// Seeds a confirmed Order — used by the cancel-mid-lifecycle test
    /// which needs to start from the Confirmed state (Cancel from Pending
    /// is a perfectly valid path, but the plan's example walks Confirmed
    /// → Cancelled).
    /// </summary>
    private async Task<Guid> SeedConfirmedOrderAsync()
    {
        var orderId = await SeedPendingOrderAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var order = await dbContext.Orders.IgnoreQueryFilters().SingleOrDefaultAsync(o => o.Id == OrderId.Of(orderId));
        order.Should().NotBeNull();

        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Confirmed);

        await dbContext.SaveChangesAsync();
        return orderId;
    }

    [Fact]
    public async Task FullLifecycle_Pending_To_Delivered()
    {
        var client = NewKitchenClient();
        var orderId = await SeedPendingOrderAsync();

        // Pending → Confirmed
        var confirm = await client.PostAsync($"/v1/orders/{orderId}/confirm", content: null);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"confirm body: {await confirm.Content.ReadAsStringAsync()}");

        // Confirmed → Preparing
        var startPrep = await client.PostAsync($"/v1/orders/{orderId}/start-prep", content: null);
        startPrep.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"start-prep body: {await startPrep.Content.ReadAsStringAsync()}");

        // Preparing → Ready
        var markReady = await client.PostAsync($"/v1/orders/{orderId}/mark-ready", content: null);
        markReady.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"mark-ready body: {await markReady.Content.ReadAsStringAsync()}");

        // Ready → Delivered
        var markDelivered = await client.PostAsync($"/v1/orders/{orderId}/mark-delivered", content: null);
        markDelivered.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"mark-delivered body: {await markDelivered.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task MarkOrderDelivered_FromReady_Returns204()
    {
        // Force the order directly into Ready so the only remaining
        // transition is Ready → Delivered.
        var orderId = await SeedPendingOrderAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var order = await dbContext.Orders.IgnoreQueryFilters().SingleOrDefaultAsync(o => o.Id == OrderId.Of(orderId));
            order.Should().NotBeNull();
            order.GetType().GetProperty(nameof(Order.Status))!
                .SetValue(order, OrderStatus.Ready);
            await dbContext.SaveChangesAsync();
        }

        var client = NewKitchenClient();
        var response = await client.PostAsync($"/v1/orders/{orderId}/mark-delivered", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task CancelOrder_AfterConfirm_Returns204()
    {
        var orderId = await SeedConfirmedOrderAsync();

        var client = NewKitchenClient();
        var response = await client.PostAsJsonAsync(
            $"/v1/orders/{orderId}/cancel",
            new { reason = "customer requested" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }
}
