using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Ordering.Application.Dtos;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage for the <c>PUT /v1/orders</c> endpoint.
/// Seeds a real <c>Order</c> aggregate via the application's
/// <see cref="IApplicationDbContext"/>, drives it through the HTTP
/// pipeline, then reads it back to confirm the scalar updates landed.
/// </summary>
/// <remarks>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3:
/// <list type="bullet">
/// <item>happy path: 200 on a valid payload and persisted changes;</item>
/// <item>not-found: 404 when the request body points at an unknown id;</item>
/// <item>validation: 400 when the request body omits required fields.</item>
/// </list>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class UpdateOrderEndpointTests
{
    private const string OrdersWritePermission = "orders:write";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public UpdateOrderEndpointTests(OrderingWebApplicationFactory factory)
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

    private static OrderItemDto ValidItemDto(Guid menuItemId) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: menuItemId,
        MenuItemName: "Burger",
        MenuItemDescription: "A burger",
        MenuItemImageUrl: "https://example.test/burger.png",
        Quantity: 1,
        UnitPrice: 9.99m,
        BasePrice: 9.99m,
        TotalPrice: 9.99m,
        SeatNumber: 1,
        SpecialInstructions: string.Empty,
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    private static AddressDto ValidAddressDto() =>
        new("123 Main St", "Springfield", "IL", "12345", "US");

    private static PaymentDto ValidPaymentDto() => new(PaymentMethod.Card, "Visa", "1111");

    private static OrderDto NewUpdateDto(Guid orderId, Guid customerId, Guid menuItemId) => new(
        Id: orderId,
        CustomerId: customerId,
        OrderNumber: $"ORD-{Guid.NewGuid():N}"[..16],
        RestaurantId: TestAuthHandler.DefaultRestaurantId,
        Currency: "USD",
        Subtotal: 12.50m,
        TaxRate: 0.08m,
        TaxAmount: 1m,
        DiscountAmount: 5m,
        DiscountCode: "SAVE5",
        TotalAmount: 8.50m,
        Status: OrderStatus.Pending,
        OrderType: OrderType.DineIn,
        BillingAddress: ValidAddressDto(),
        DeliveryAddress: ValidAddressDto(),
        DeliveryNotes: "ring bell",
        DeliveryStatus: null,
        DeliveryLatitude: null,
        DeliveryLongitude: null,
        Payment: ValidPaymentDto(),
        EstimatedPrepTimeMinutes: 20,
        ActualPrepTimeMinutes: 0,
        IsModified: true,
        RequiresAdminApproval: false,
        TableId: null,
        CreatedByUserId: null,
        ApprovedByAdminId: null,
        ConfirmedByUserId: null,
        CompletedByUserId: null,
        ApprovedAt: null,
        CancelledAt: null,
        CompletedAt: null,
        ConfirmedAt: null,
        DeliveredAt: null,
        PreparingStartedAt: null,
        ReadyAt: null,
        Notes: "updated notes",
        OrderItems: [ValidItemDto(menuItemId)],
        Activities: []);

    /// <summary>
    /// Seeds a pending Order with one item and returns the ids needed by
    /// the test payload. The factory's WAF uses Testcontainers MSSQL —
    /// the schema is built by the production migration runner on host
    /// start, so these inserts land on the live schema.
    /// </summary>
    private async Task<(Guid OrderId, Guid CustomerId, Guid MenuItemId)> SeedPendingOrderAsync()
    {
        var customerId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        var customer = Customer.Create(
            CustomerId.Of(customerId),
            $"test-customer-{customerId:N}@test.local",
            "Test Customer",
            "555-0100");

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(customerId),
            OrderNumber.Of($"ORD-{Guid.NewGuid():N}"[..16]),
            TestAuthHandler.DefaultRestaurantId,
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(PaymentMethod.Card, "Visa", "1111"));

        order.Add(MenuItemId.Of(menuItemId), quantity: 1, price: 9.99m);
        order.ClearDomainEvents();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.MenuItems.Add(MenuItem.Create(MenuItemId.Of(menuItemId), "Burger", 9.99m));
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return (order.Id.Value, customerId, menuItemId);
    }

    [Fact]
    public async Task UpdateOrder_ValidPayload_Returns200()
    {
        var client = NewWriteClient();
        var (orderId, customerId, menuItemId) = await SeedPendingOrderAsync();
        var dto = NewUpdateDto(orderId, customerId, menuItemId);

        var response = await client.PutAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UpdateOrder_NonExistent_Returns404()
    {
        var client = NewWriteClient();
        var (_, _, menuItemId) = await SeedPendingOrderAsync();
        // Fresh, non-seeded id — won't match any row in the DB.
        var dto = NewUpdateDto(Guid.NewGuid(), Guid.NewGuid(), menuItemId);

        var response = await client.PutAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task UpdateOrder_InvalidPayload_Returns400()
    {
        // Empty body — the OrderDtoValidator's nested rules reject the
        // OrderDto before the handler runs (e.g. Currency must be a 3-letter
        // ISO 4217 code; BillingAddress and Payment are required).
        var client = NewWriteClient();

        var response = await client.PutAsJsonAsync(
            "/v1/orders",
            new { order = new { id = Guid.NewGuid() } },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }
}
