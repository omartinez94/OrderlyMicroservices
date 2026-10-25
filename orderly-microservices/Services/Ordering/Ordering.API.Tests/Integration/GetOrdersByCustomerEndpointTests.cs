using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage for the
/// <c>GET /v1/orders/customer/{customerId}</c> endpoint. The handler
/// returns an empty <c>Orders</c> collection (not 404) when the seeded
/// customer has no orders — that's the production behavior and matches
/// the unit-test contract in <c>GetOrdersByCustomerHandlerTests</c>.
/// </summary>
/// <remarks>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3:
/// <list type="bullet">
/// <item>happy path: 200 with the orders list when the customer has
/// seeded orders;</item>
/// <item>empty: 200 with an empty list when the customer has no orders.</item>
/// </list>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class GetOrdersByCustomerEndpointTests
{
    private const string OrdersViewOwnPermission = "orders:view_own";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public GetOrdersByCustomerEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewReadClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", OrdersViewOwnPermission);
        return client;
    }

    /// <summary>
    /// Seeds a customer with exactly <paramref name="orderCount"/> pending
    /// orders and returns the customer's id. The factory's WAF uses
    /// Testcontainers MSSQL — the schema is built by the production
    /// migration runner on host start, so these inserts land on the live
    /// schema.
    /// </summary>
    private async Task<Guid> SeedCustomerWithOrdersAsync(int orderCount)
    {
        var customerId = CustomerId.Of(Guid.NewGuid());
        var menuItemId = MenuItemId.Of(Guid.NewGuid());

        var customer = Customer.Create(
            customerId,
            $"test-customer-{customerId.Value:N}@test.local",
            "Test Customer",
            "555-0100");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(MenuItem.Create(menuItemId, "Burger", 9.99m));

        for (var i = 0; i < orderCount; i++)
        {
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

            dbContext.Orders.Add(order);
        }

        await dbContext.SaveChangesAsync();

        return customerId.Value;
    }

    [Fact]
    public async Task GetOrdersByCustomer_WithOrders_Returns200()
    {
        var client = NewReadClient();
        var customerId = await SeedCustomerWithOrdersAsync(orderCount: 2);

        var response = await client.GetAsync($"/v1/orders/customer/{customerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orders = body.GetProperty("orders");
        orders.GetArrayLength().Should().Be(2);
        foreach (var order in orders.EnumerateArray())
        {
            order.GetProperty("customerId").GetGuid().Should().Be(customerId);
        }
    }

    [Fact]
    public async Task GetOrdersByCustomer_NoOrders_Returns200EmptyList()
    {
        // Seed a customer with zero orders — the query targets this
        // newly-created customer, so the filter has nothing to return.
        var client = NewReadClient();
        var customerId = await SeedCustomerWithOrdersAsync(orderCount: 0);

        var response = await client.GetAsync($"/v1/orders/customer/{customerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("orders").GetArrayLength().Should().Be(0);
    }
}
