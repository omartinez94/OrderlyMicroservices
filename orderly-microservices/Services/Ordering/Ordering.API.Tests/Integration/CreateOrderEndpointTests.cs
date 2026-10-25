using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Ordering.Application.Dtos;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end happy-path + validation coverage for the
/// <c>POST /v1/orders</c> endpoint. Each test seeds the necessary
/// owning rows (Customer, MenuItem) via the application's
/// <see cref="IApplicationDbContext"/> so the order's foreign keys
/// resolve, then exercises the HTTP pipeline through the
/// <see cref="OrderingWebApplicationFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3 the
/// "with discount" assertion verifies the discount scalar fields
/// (<c>DiscountAmount</c>, <c>DiscountCode</c>, <c>TotalAmount</c>) flow
/// from the request body into the persisted <c>Order</c> — the handler
/// applies them directly to the aggregate, so the read-back via
/// <c>GET /v1/orders/{id}</c> proves the persistence path.
/// </para>
/// <para>
/// <b>Authorization note</b>: the endpoint requires
/// <c>orders:write</c>; the negative paths already covered in
/// <see cref="OrderingAuthorizationEnforcementTests"/> are not duplicated
/// here.
/// </para>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class CreateOrderEndpointTests
{
    private const string OrdersWritePermission = "orders:write";
    private const string OrdersViewPermission = "orders:view_own";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingWebApplicationFactory _factory;

    public CreateOrderEndpointTests(OrderingWebApplicationFactory factory)
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

    private HttpClient NewReadClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", OrdersViewPermission);
        return client;
    }

    private static OrderItemDto ValidItemDto(Guid menuItemId) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: menuItemId,
        MenuItemName: "Burger",
        MenuItemDescription: "A burger",
        MenuItemImageUrl: "https://example.test/burger.png",
        Quantity: 2,
        UnitPrice: 9.99m,
        BasePrice: 9.99m,
        TotalPrice: 19.98m,
        SeatNumber: 1,
        SpecialInstructions: "no onions",
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    private static OrderItemDto[] ValidItems(Guid[] menuItemIds) =>
        menuItemIds.Select(ValidItemDto).ToArray();

    private static AddressDto ValidAddressDto() =>
        new("123 Main St", "Springfield", "IL", "12345", "US");

    private static PaymentDto ValidPaymentDto() => new(PaymentMethod.Card, "Visa", "1111");

    private static OrderDto NewOrderDto(
        Guid customerId,
        Guid restaurantId,
        IReadOnlyList<OrderItemDto> items,
        decimal subtotal = 19.98m,
        decimal taxAmount = 1.60m,
        decimal discountAmount = 0m,
        string discountCode = "",
        decimal totalAmount = 21.58m,
        string? orderNumber = null,
        OrderType orderType = OrderType.DineIn) => new(
            Id: Guid.NewGuid(),
            CustomerId: customerId,
            OrderNumber: orderNumber ?? $"ORD-{Guid.NewGuid():N}"[..16],
            RestaurantId: restaurantId,
            Currency: "USD",
            Subtotal: subtotal,
            TaxRate: 0.08m,
            TaxAmount: taxAmount,
            DiscountAmount: discountAmount,
            DiscountCode: discountCode,
            TotalAmount: totalAmount,
            Status: OrderStatus.Pending,
            OrderType: orderType,
            BillingAddress: ValidAddressDto(),
            DeliveryAddress: ValidAddressDto(),
            DeliveryNotes: "leave at door",
            DeliveryStatus: null,
            DeliveryLatitude: null,
            DeliveryLongitude: null,
            Payment: ValidPaymentDto(),
            EstimatedPrepTimeMinutes: 15,
            ActualPrepTimeMinutes: 0,
            IsModified: false,
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
            Notes: "extra napkins",
            OrderItems: items,
            Activities: []);

    /// <summary>
    /// Seeds the owning <c>Customer</c> + <c>MenuItem</c> rows and returns
    /// their ids. The factory's WAF uses Testcontainers MSSQL — the schema
    /// is built by the production migration runner on host start, so these
    /// inserts land on the live schema.
    /// </summary>
    private async Task<(Guid CustomerId, Guid[] MenuItemIds)> SeedOwnerAsync(int menuItemCount)
    {
        var customerId = Guid.NewGuid();
        var menuItemIds = Enumerable.Range(0, menuItemCount).Select(_ => Guid.NewGuid()).ToArray();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var customer = Customer.Create(
            CustomerId.Of(customerId),
            $"test-customer-{customerId:N}@test.local",
            "Test Customer",
            "555-0100");
        dbContext.Customers.Add(customer);

        foreach (var menuItemId in menuItemIds)
        {
            dbContext.MenuItems.Add(MenuItem.Create(MenuItemId.Of(menuItemId), "Burger", 9.99m));
        }

        await dbContext.SaveChangesAsync();

        return (customerId, menuItemIds);
    }

    [Fact]
    public async Task CreateOrder_ValidPayload_Returns201WithOrderId()
    {
        var client = NewWriteClient();
        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        var dto = NewOrderDto(customerId, Guid.NewGuid(), ValidItems(menuItemIds));

        var response = await client.PostAsJsonAsync(
            "/v1/orders",
            new { order = dto },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response.Content.ReadAsStringAsync()}");

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/v1/orders/");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateOrder_MissingCustomer_Returns400()
    {
        // CustomerId is Guid.Empty — the OrderDtoValidator's CustomerId
        // NotEmpty rule rejects the request before the handler runs.
        var client = NewWriteClient();
        var (_, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        var dto = NewOrderDto(
            customerId: Guid.Empty,
            Guid.NewGuid(),
            ValidItems(menuItemIds));

        var response = await client.PostAsJsonAsync(
            "/v1/orders",
            new { order = dto },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_Returns400()
    {
        // OrderDtoValidator's OrderItems NotEmpty rule rejects the request.
        var client = NewWriteClient();
        var (customerId, _) = await SeedOwnerAsync(menuItemCount: 0);
        var dto = NewOrderDto(customerId, Guid.NewGuid(), Array.Empty<OrderItemDto>());

        var response = await client.PostAsJsonAsync(
            "/v1/orders",
            new { order = dto },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task CreateOrder_WithDiscount_PersistsDiscountOnBill()
    {
        // The handler maps DiscountAmount / DiscountCode / TotalAmount
        // directly onto the Order aggregate; the read-back via
        // GET /v1/orders/{id} (with read permission) confirms the
        // values landed in the persisted row.
        const decimal discountAmount = 5m;
        const string discountCode = "SAVE5";
        const decimal subtotal = 19.98m;
        const decimal taxAmount = 1.60m;
        const decimal totalAmount = 16.58m; // subtotal + tax - discount

        var client = NewWriteClient();
        var readClient = NewReadClient();
        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        var dto = NewOrderDto(
            customerId,
            TestAuthHandler.DefaultRestaurantId,
            ValidItems(menuItemIds),
            subtotal: subtotal,
            taxAmount: taxAmount,
            discountAmount: discountAmount,
            discountCode: discountCode,
            totalAmount: totalAmount);

        var response = await client.PostAsJsonAsync(
            "/v1/orders",
            new { order = dto },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderId = body.GetProperty("id").GetGuid();

        var getResponse = await readClient.GetAsync($"/v1/orders/{orderId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // GET /v1/orders/{id} wraps the OrderDto in a `order` property
        // (see GetOrderByIdResponse record). Drill in one level before
        // asserting on the bill fields.
        var order = getBody.GetProperty("order");
        order.GetProperty("discountAmount").GetDecimal().Should().Be(discountAmount);
        order.GetProperty("discountCode").GetString().Should().Be(discountCode);
        order.GetProperty("totalAmount").GetDecimal().Should().Be(totalAmount);
        order.GetProperty("subtotal").GetDecimal().Should().Be(subtotal);
    }

    [Fact]
    public async Task CreateOrder_WithEmptyOrderNumber_GeneratesDailySequenceOrderNumber()
    {
        var client = NewWriteClient();
        var readClient = NewReadClient();
        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        var restaurantId = Guid.NewGuid();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.ToString());
        readClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.ToString());

        var dto1 = NewOrderDto(
            customerId,
            restaurantId,
            ValidItems(menuItemIds),
            orderNumber: string.Empty,
            orderType: OrderType.DineIn);

        var response1 = await client.PostAsJsonAsync("/v1/orders", new { order = dto1 }, JsonOptions);
        response1.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response1.Content.ReadAsStringAsync()}");

        var body1 = await response1.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderId1 = body1.GetProperty("id").GetGuid();

        var getResponse1 = await readClient.GetAsync($"/v1/orders/{orderId1}");
        getResponse1.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody1 = await getResponse1.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderNumber1 = getBody1.GetProperty("order").GetProperty("orderNumber").GetString();

        orderNumber1.Should().NotBeNullOrWhiteSpace();
        orderNumber1.Should().StartWith("DIN");
        orderNumber1.Should().EndWith("-0001");

        // Create second order for same restaurant with Takeout
        var dto2 = NewOrderDto(
            customerId,
            restaurantId,
            ValidItems(menuItemIds),
            orderNumber: string.Empty,
            orderType: OrderType.Takeout);

        var response2 = await client.PostAsJsonAsync("/v1/orders", new { order = dto2 }, JsonOptions);
        response2.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response2.Content.ReadAsStringAsync()}");

        var body2 = await response2.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderId2 = body2.GetProperty("id").GetGuid();

        var getResponse2 = await readClient.GetAsync($"/v1/orders/{orderId2}");
        getResponse2.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody2 = await getResponse2.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderNumber2 = getBody2.GetProperty("order").GetProperty("orderNumber").GetString();

        orderNumber2.Should().NotBeNullOrWhiteSpace();
        orderNumber2.Should().StartWith("TAK");
        orderNumber2.Should().EndWith("-0002");
    }
}
