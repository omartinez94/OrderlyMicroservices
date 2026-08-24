using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage for the per-item prep-state transitions:
/// <c>POST /v1/orders/{id}/items/{itemId}/start-prep</c> and
/// <c>POST /v1/orders/{id}/items/{itemId}/mark-ready</c>.
/// </summary>
/// <remarks>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3:
/// <list type="bullet">
/// <item>happy path: 204 on both Pending→Preparing and Preparing→Ready;</item>
/// <item>invalid-state: 400 on the illegal transition (the plan called
/// for 400; the global exception handler maps
/// <c>InvalidOrderItemStateTransitionException</c> to 409 Conflict —
/// the tests assert the production behavior and document the deviation).</item>
/// </list>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class ItemTransitionEndpointTests
{
    private const string KitchenPermissions = "kitchen:update_prep_status";

    private readonly OrderingWebApplicationFactory _factory;

    public ItemTransitionEndpointTests(OrderingWebApplicationFactory factory)
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
    /// Seeds a confirmed Order with one item whose PrepStatus is
    /// <paramref name="itemStatus"/>. Returns the order id and the seeded
    /// item id. The factory's WAF uses Testcontainers MSSQL — the schema
    /// is built by the production migration runner on host start, so these
    /// inserts land on the live schema.
    /// </summary>
    private async Task<(Guid OrderId, Guid ItemId)> SeedOrderWithItemAsync(PrepStatus itemStatus)
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
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "1111"));

        order.Add(menuItemId, quantity: 1, price: 9.99m);
        order.OrderItems.Single().PrepStatus = itemStatus;

        // Force the order status to Confirmed (Create sets Pending). The
        // Aggregate exposes Status as a settable property for test seeder
        // scenarios; production callers always go through Confirm.
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Confirmed);

        order.ClearDomainEvents();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(MenuItem.Create(menuItemId, "Burger", 9.99m));
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return (order.Id.Value, order.OrderItems.Single().Id.Value);
    }

    [Fact]
    public async Task StartItemPrep_ValidItem_Returns204()
    {
        var client = NewKitchenClient();
        var (orderId, itemId) = await SeedOrderWithItemAsync(PrepStatus.Pending);

        var response = await client.PostAsync(
            $"/v1/orders/{orderId}/items/{itemId}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task MarkItemReady_AfterPrep_Returns204()
    {
        var client = NewKitchenClient();
        var (orderId, itemId) = await SeedOrderWithItemAsync(PrepStatus.Preparing);

        var response = await client.PostAsync(
            $"/v1/orders/{orderId}/items/{itemId}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task StartItemPrep_AlreadyPreparing_Returns409()
    {
        // The aggregate's MarkItemPreparing guard throws
        // InvalidOrderItemStateTransitionException when the item is
        // already Preparing. The global exception handler maps
        // *StateTransitionException to 409 Conflict (not 400 as the plan
        // spec originally called for — see <see cref="ItemTransitionEndpointTests"/>
        // remarks).
        var client = NewKitchenClient();
        var (orderId, itemId) = await SeedOrderWithItemAsync(PrepStatus.Preparing);

        var response = await client.PostAsync(
            $"/v1/orders/{orderId}/items/{itemId}/start-prep",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task MarkItemReady_BeforePrep_Returns409()
    {
        // The item is Pending — MarkItemReady requires Preparing. The
        // 409 path mirrors StartItemPrep_AlreadyPreparing (the global
        // exception handler maps *StateTransitionException to 409).
        var client = NewKitchenClient();
        var (orderId, itemId) = await SeedOrderWithItemAsync(PrepStatus.Pending);

        var response = await client.PostAsync(
            $"/v1/orders/{orderId}/items/{itemId}/mark-ready",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }
}
