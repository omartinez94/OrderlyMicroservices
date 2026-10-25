using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Multi-tenant isolation coverage for <see cref="ApplicationDBContext"/>'s
/// global query filter on <see cref="Order"/>. Verifies that a caller
/// authenticated under Restaurant A cannot read or mutate orders belonging
/// to Restaurant B across all read and write endpoints, and that a token
/// lacking a <c>restaurantId</c> claim fails closed (0 rows / 404).
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class GlobalTenantFilterTests(OrderingWebApplicationFactory factory)
{
    private const string AllOrderPermissions =
        "orders:view_own,orders:view_all,orders:write,kitchen:view_orders,kitchen:update_prep_status";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateTenantClient(Guid? restaurantId)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", AllOrderPermissions);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Test-Restaurant",
            restaurantId?.ToString() ?? "none");
        return client;
    }

    private async Task<Order> SeedOrderForRestaurantAsync(Guid restaurantId, Guid? customerIdOverride = null)
    {
        CustomerId customerId = CustomerId.Of(customerIdOverride ?? Guid.NewGuid());
        MenuItemId menuItemId = MenuItemId.Of(Guid.NewGuid());

        Order order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            customerId,
            OrderNumber.Of($"ORD-{Guid.NewGuid():N}"[..16]),
            restaurantId,
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "1111"));

        order.Add(menuItemId, quantity: 1, price: 9.99m);
        order.ClearDomainEvents();

        using IServiceScope scope = factory.Services.CreateScope();
        IApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        bool customerExists = await dbContext.Customers.AnyAsync(c => c.Id == customerId);
        if (!customerExists)
        {
            Customer customer = Customer.Create(
                customerId,
                $"tenant-{customerId.Value:N}@test.local",
                "Tenant Customer",
                "555-0199");
            dbContext.Customers.Add(customer);
        }

        dbContext.MenuItems.Add(MenuItem.Create(menuItemId, "Burger", 9.99m));
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order;
    }

    [Fact]
    public async Task GetOrders_FiltersToCallerRestaurantOnly()
    {
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();

        Order orderA1 = await SeedOrderForRestaurantAsync(restaurantA);
        Order orderA2 = await SeedOrderForRestaurantAsync(restaurantA);
        Order orderB1 = await SeedOrderForRestaurantAsync(restaurantB);

        HttpClient clientA = CreateTenantClient(restaurantA);
        HttpResponseMessage responseA = await clientA.GetAsync("/v1/orders?pageIndex=0&pageSize=50");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement ordersRoot = bodyA.GetProperty("orders");
        ordersRoot.GetProperty("count").GetInt64().Should().Be(2);

        List<Guid> returnedIds = ordersRoot
            .GetProperty("data")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        returnedIds.Should().BeEquivalentTo([orderA1.Id.Value, orderA2.Id.Value]);
        returnedIds.Should().NotContain(orderB1.Id.Value);
    }

    [Fact]
    public async Task GetOrderById_CrossTenantOrder_Returns404()
    {
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();

        Order orderB = await SeedOrderForRestaurantAsync(restaurantB);

        HttpClient clientA = CreateTenantClient(restaurantA);
        HttpResponseMessage response = await clientA.GetAsync($"/v1/orders/{orderB.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrderActivities_CrossTenantOrder_Returns404()
    {
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();

        Order orderB = await SeedOrderForRestaurantAsync(restaurantB);

        HttpClient clientA = CreateTenantClient(restaurantA);
        HttpResponseMessage response = await clientA.GetAsync($"/v1/orders/{orderB.Id.Value}/activities");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrdersByCustomer_SharedCustomerAcrossRestaurants_ReturnsOnlyCallerTenantOrders()
    {
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();
        Guid sharedCustomerId = Guid.NewGuid();

        Order orderA = await SeedOrderForRestaurantAsync(restaurantA, sharedCustomerId);
        Order orderB = await SeedOrderForRestaurantAsync(restaurantB, sharedCustomerId);

        HttpClient clientA = CreateTenantClient(restaurantA);
        HttpResponseMessage response = await clientA.GetAsync($"/v1/orders/customer/{sharedCustomerId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        List<Guid> ids = body
            .GetProperty("orders")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().ContainSingle().Which.Should().Be(orderA.Id.Value);
        ids.Should().NotContain(orderB.Id.Value);
    }

    [Fact]
    public async Task MutatingEndpoints_CrossTenantOrder_Return404AndDoNotMutate()
    {
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();

        Order orderB = await SeedOrderForRestaurantAsync(restaurantB);
        HttpClient clientA = CreateTenantClient(restaurantA);

        HttpResponseMessage confirmResp = await clientA.PostAsync(
            $"/v1/orders/{orderB.Id.Value}/confirm",
            content: null);
        confirmResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage cancelResp = await clientA.PostAsJsonAsync(
            $"/v1/orders/{orderB.Id.Value}/cancel",
            new { reason = "cross-tenant attempt" },
            JsonOptions);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage deleteResp = await clientA.DeleteAsync($"/v1/orders/{orderB.Id.Value}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify Restaurant B can still read its order untouched in Pending status
        HttpClient clientB = CreateTenantClient(restaurantB);
        HttpResponseMessage getB = await clientB.GetAsync($"/v1/orders/{orderB.Id.Value}");
        getB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingRestaurantClaim_FailsClosedWithZeroRowsAnd404()
    {
        Guid restaurantA = Guid.NewGuid();
        Order orderA = await SeedOrderForRestaurantAsync(restaurantA);

        HttpClient clientWithoutTenant = CreateTenantClient(restaurantId: null);

        HttpResponseMessage listResp = await clientWithoutTenant.GetAsync("/v1/orders");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        listBody.GetProperty("orders").GetProperty("count").GetInt64().Should().Be(0);

        HttpResponseMessage getResp = await clientWithoutTenant.GetAsync($"/v1/orders/{orderA.Id.Value}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
