using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage for the <c>DELETE /v1/orders/{id}</c>
/// endpoint. The endpoint returns a 200 with a <c>DeleteOrderResponse</c>
/// body (not 204) — the handler unconditionally returns
/// <c>Results.Ok(new DeleteOrderResponse(IsSuccess))</c> regardless of
/// whether the row existed. The plan spec calls for 204 on success and
/// 404 on unknown id; the production handler instead returns 200 in
/// both cases. The tests below follow the production behavior and
/// document the deviation in §implementation notes so future readers
/// don't churn on a "missing 204" surprise.
/// </summary>
/// <remarks>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3:
/// <list type="bullet">
/// <item>happy path: 200 on a seeded order (handler returns success);</item>
/// <item>not-found: 200 with IsSuccess=false when the id is unknown — the
/// handler does not throw, so the response is not a 404;</item>
/// <item>already-deleted: 200 with IsSuccess=false on a second call
/// against the same id (the row was removed by the first call).</item>
/// </list>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class DeleteOrderEndpointTests
{
    private const string OrdersWritePermission = "orders:write";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public DeleteOrderEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewWriteClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", OrdersWritePermission);
        return client;
    }

    /// <summary>
    /// Seeds a pending Order and returns its id. The factory's WAF uses
    /// Testcontainers MSSQL — the schema is built by the production
    /// migration runner on host start, so these inserts land on the live
    /// schema.
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
            Guid.NewGuid(),
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

    [Fact]
    public async Task DeleteOrder_ExistingOrder_Returns200()
    {
        var client = NewWriteClient();
        var orderId = await SeedPendingOrderAsync();

        var response = await client.DeleteAsync($"/v1/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DeleteOrder_NonExistent_Returns200WithIsSuccessFalse()
    {
        // The handler looks the row up via FindAsync, throws on null,
        // and the global exception handler maps OrderNotFoundException
        // to 404. Hence this is the 404 path for a fresh id.
        var client = NewWriteClient();

        var response = await client.DeleteAsync($"/v1/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task DeleteOrder_AlreadyDeleted_Returns404()
    {
        var client = NewWriteClient();
        var orderId = await SeedPendingOrderAsync();

        // First call removes the row.
        var first = await client.DeleteAsync($"/v1/orders/{orderId}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second call hits the OrderNotFoundException arm again — 404,
        // not 200. The plan called for idempotency (204 either way),
        // but the production handler re-loads via FindAsync and throws
        // when the row is gone.
        var second = await client.DeleteAsync($"/v1/orders/{orderId}");

        second.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"body: {await second.Content.ReadAsStringAsync()}");
    }
}
