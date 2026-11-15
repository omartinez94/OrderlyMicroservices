using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Ordering.API.Endpoints;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Data;
using Ordering.Application.Dtos;
using Ordering.Domain.Enums;
using Ordering.Domain.Models;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Integration tests verifying POS order creation workflows, contract validations,
/// server-side price/availability enforcement, tenant isolation, and idempotency.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class CreatePosOrderTests : IDisposable
{
    private const string OrdersWritePermission = "orders:write";
    private const string OrdersViewPermission = "orders:view_own";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    }

    private readonly OrderingWebApplicationFactory _factory;

    public CreatePosOrderTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
        TestCatalogPricingClient.Reset();
    }

    public void Dispose()
    {
        TestCatalogPricingClient.Reset();
    }

    private HttpClient CreateWriteClient(Guid? restaurantId = null)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", OrdersWritePermission);
        if (restaurantId.HasValue)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.Value.ToString());
        }
        return client;
    }

    private HttpClient CreateReadClient(Guid? restaurantId = null)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", OrdersViewPermission);
        if (restaurantId.HasValue)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.Value.ToString());
        }
        return client;
    }

    private static OrderItemDto CreateItemDto(
        Guid menuItemId,
        int seatNumber = 1,
        decimal unitPrice = 12.50m,
        int quantity = 1,
        decimal? totalPrice = null) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: menuItemId,
        MenuItemName: "Artisan Burger",
        MenuItemDescription: "Freshly prepared artisan burger",
        MenuItemImageUrl: "https://example.test/artisan-burger.png",
        Quantity: quantity,
        UnitPrice: unitPrice,
        BasePrice: unitPrice,
        TotalPrice: totalPrice ?? (unitPrice * quantity),
        SeatNumber: seatNumber,
        SpecialInstructions: "Extra crispy bacon",
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    private static OrderDto CreateOrderDto(
        Guid customerId,
        Guid restaurantId,
        IReadOnlyList<OrderItemDto> items,
        OrderType orderType = OrderType.DineIn,
        Guid? tableId = null,
        PaymentDto? payment = null,
        decimal subtotal = 12.50m,
        decimal taxRate = 0.08m,
        decimal taxAmount = 1.00m,
        decimal totalAmount = 13.50m) => new(
        Id: Guid.NewGuid(),
        CustomerId: customerId,
        OrderNumber: string.Empty,
        RestaurantId: restaurantId,
        Currency: "USD",
        Subtotal: subtotal,
        TaxRate: taxRate,
        TaxAmount: taxAmount,
        DiscountAmount: 0m,
        DiscountCode: string.Empty,
        TotalAmount: totalAmount,
        Status: OrderStatus.Pending,
        OrderType: orderType,
        BillingAddress: null,
        DeliveryAddress: null,
        DeliveryNotes: string.Empty,
        DeliveryStatus: null,
        DeliveryLatitude: null,
        DeliveryLongitude: null,
        Payment: payment,
        EstimatedPrepTimeMinutes: 15,
        ActualPrepTimeMinutes: 0,
        IsModified: false,
        RequiresAdminApproval: false,
        TableId: tableId,
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
        Notes: "POS terminal 1",
        OrderItems: items,
        Activities: []);

    private async Task<(Guid CustomerId, Guid[] MenuItemIds)> SeedOwnerAsync(int menuItemCount, decimal itemPrice = 12.50m)
    {
        Guid customerId = Guid.NewGuid();
        Guid[] menuItemIds = Enumerable.Range(0, menuItemCount).Select(_ => Guid.NewGuid()).ToArray();

        using IServiceScope scope = _factory.Services.CreateScope();
        IApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Customer customer = Customer.Create(
            CustomerId.Of(customerId),
            $"pos-customer-{customerId:N}@orderly.local",
            "POS Test Customer",
            "555-0199");
        dbContext.Customers.Add(customer);

        foreach (Guid menuItemId in menuItemIds)
        {
            dbContext.MenuItems.Add(MenuItem.Create(MenuItemId.Of(menuItemId), "Artisan Burger", itemPrice));
        }

        await dbContext.SaveChangesAsync();

        return (customerId, menuItemIds);
    }

    [Fact]
    public async Task CreatePosOrder_DineIn_SeatsCoursesDeferredPayment_Returns201WithCreatedOrderShape()
    {
        // Scenario 1: Dine-In order with seats, course numbers, and deferred/omitted payment -> returns 201 Created
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);
        HttpClient readClient = CreateReadClient(restaurantId);

        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 2);

        OrderItemDto item1 = CreateItemDto(menuItemIds[0], seatNumber: 1, unitPrice: 12.50m);
        OrderItemDto item2 = CreateItemDto(menuItemIds[1], seatNumber: 2, unitPrice: 12.50m);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: restaurantId,
            items: [item1, item2],
            orderType: OrderType.DineIn,
            tableId: tableId,
            payment: null, // Deferred / omitted payment
            subtotal: 25.00m,
            taxRate: 0.08m,
            taxAmount: 2.00m,
            totalAmount: 27.00m);

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response.Content.ReadAsStringAsync()}");
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/v1/orders/");

        CreateOrderResponse? createResult = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
        createResult.Should().NotBeNull();
        createResult!.Id.Should().NotBe(Guid.Empty);

        // Read back the created order to verify exact shape
        HttpResponseMessage getResponse = await readClient.GetAsync($"/v1/orders/{createResult.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement order = getBody.GetProperty("order");

        order.GetProperty("id").GetGuid().Should().Be(createResult.Id);
        order.GetProperty("orderType").GetString().Should().Be(nameof(OrderType.DineIn));
        order.GetProperty("tableId").GetGuid().Should().Be(tableId);
        order.GetProperty("status").GetString().Should().Be(nameof(OrderStatus.Pending));

        // Deferred payment should project to null in wire DTO
        order.GetProperty("payment").ValueKind.Should().Be(JsonValueKind.Null);

        // Verify seats persisted on items
        JsonElement items = order.GetProperty("orderItems");
        items.GetArrayLength().Should().Be(2);

        JsonElement readItem1 = items.EnumerateArray().First(i => i.GetProperty("menuItemId").GetGuid() == menuItemIds[0]);
        readItem1.GetProperty("seatNumber").GetInt32().Should().Be(1);

        JsonElement readItem2 = items.EnumerateArray().First(i => i.GetProperty("menuItemId").GetGuid() == menuItemIds[1]);
        readItem2.GetProperty("seatNumber").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task CreatePosOrder_TakeoutWalkIn_WithEmptyCustomerId_Returns201WithSubstitutedWalkInCustomerId()
    {
        // Scenario 2: Takeout walk-in with CustomerId = Guid.Empty -> returns 201 Created with substituted walk-in customer ID
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);
        HttpClient readClient = CreateReadClient(restaurantId);

        var (_, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);

        OrderItemDto item = CreateItemDto(menuItemIds[0], seatNumber: 0);

        OrderDto dto = CreateOrderDto(
            customerId: Guid.Empty, // Walk-in counter customer
            restaurantId: restaurantId,
            items: [item],
            orderType: OrderType.Takeout,
            tableId: null,
            payment: null);

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response.Content.ReadAsStringAsync()}");

        CreateOrderResponse? createResult = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
        createResult.Should().NotBeNull();

        HttpResponseMessage getResponse = await readClient.GetAsync($"/v1/orders/{createResult!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement order = getBody.GetProperty("order");

        Guid expectedWalkInCustomerId = WalkInCustomer.IdFor(restaurantId);
        order.GetProperty("customerId").GetGuid().Should().Be(expectedWalkInCustomerId);
        order.GetProperty("orderType").GetString().Should().Be(nameof(OrderType.Takeout));
    }

    [Fact]
    public async Task CreatePosOrder_ForeignRestaurant_Returns403Forbidden()
    {
        // Scenario 3: Foreign restaurant (caller has JWT claims for restaurant A, requests restaurant B) -> returns 403 Forbidden
        Guid callerRestaurantId = Guid.NewGuid();
        Guid targetRestaurantId = Guid.NewGuid();

        HttpClient client = CreateWriteClient(callerRestaurantId);
        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);

        OrderItemDto item = CreateItemDto(menuItemIds[0], seatNumber: 0);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: targetRestaurantId, // Different restaurant than caller's claim
            items: [item],
            orderType: OrderType.Takeout);

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task CreatePosOrder_UnavailableItem_Returns409Conflict()
    {
        // Scenario 4: Unavailable item (mock Catalog returns 86'd item) -> returns 409 Conflict
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);

        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        Guid unavailableItemId = menuItemIds[0];

        // Configure mock Catalog client to return 86'd / unavailable item
        TestCatalogPricingClient.CustomMenuSnapshotHandler = (rId, _) =>
        {
            CatalogMenuItemDto item = new(
                unavailableItemId,
                "Artisan Burger",
                12.50m,
                null,
                false, // 86'd
                "SoldOut",
                []);

            return Task.FromResult(new CatalogMenuSnapshot(rId, [item]));
        };

        OrderItemDto itemDto = CreateItemDto(unavailableItemId, seatNumber: 1);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: restaurantId,
            items: [itemDto],
            orderType: OrderType.DineIn,
            tableId: tableId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"body: {await response.Content.ReadAsStringAsync()}");

        string responseBody = await response.Content.ReadAsStringAsync();
        responseBody.Should().Contain(unavailableItemId.ToString());
    }

    [Fact]
    public async Task CreatePosOrder_TamperedTotals_OverwritesWithAuthoritativeCatalogPrices()
    {
        // Scenario 5: Tampered totals (client supplies incorrect unit prices or zero total) ->
        // server overwrites prices with authoritative catalog prices and recomputes total
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);
        HttpClient readClient = CreateReadClient(restaurantId);

        const decimal catalogAuthoritativePrice = 25.00m;
        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1, itemPrice: catalogAuthoritativePrice);

        // Ensure mock Catalog returns authoritative price of $25.00
        TestCatalogPricingClient.CustomMenuSnapshotHandler = (rId, _) =>
        {
            CatalogMenuItemDto catalogItem = new(
                menuItemIds[0],
                "Artisan Burger",
                catalogAuthoritativePrice,
                null,
                true,
                "Available",
                []);

            return Task.FromResult(new CatalogMenuSnapshot(rId, [catalogItem]));
        };

        // Client maliciously sends UnitPrice = 1.00m, Subtotal = 1.00m, TotalAmount = 1.00m
        OrderItemDto tamperedItem = CreateItemDto(
            menuItemIds[0],
            seatNumber: 1,
            unitPrice: 1.00m,
            quantity: 1,
            totalPrice: 1.00m);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: restaurantId,
            items: [tamperedItem],
            orderType: OrderType.DineIn,
            tableId: tableId,
            subtotal: 1.00m,
            taxRate: 0.08m,
            taxAmount: 0m,
            totalAmount: 1.00m);

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response.Content.ReadAsStringAsync()}");

        CreateOrderResponse? createResult = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
        createResult.Should().NotBeNull();

        // Verify read back order reflects server-computed totals
        HttpResponseMessage getResponse = await readClient.GetAsync(new Uri($"/v1/orders/{createResult!.Id}", UriKind.Relative));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        JsonElement order = getBody.GetProperty("order");

        const decimal expectedSubtotal = 25.00m;
        const decimal expectedTaxAmount = 2.00m; // 25.00 * 0.08
        const decimal expectedTotalAmount = 27.00m;

        order.GetProperty("subtotal").GetDecimal().Should().Be(expectedSubtotal);
        order.GetProperty("taxAmount").GetDecimal().Should().Be(expectedTaxAmount);
        order.GetProperty("totalAmount").GetDecimal().Should().Be(expectedTotalAmount);

        JsonElement readItem = order.GetProperty("orderItems").EnumerateArray().First();
        readItem.GetProperty("unitPrice").GetDecimal().Should().Be(expectedSubtotal);
        readItem.GetProperty("totalPrice").GetDecimal().Should().Be(expectedSubtotal);
    }

    [Fact]
    public async Task CreatePosOrder_DuplicateIdempotencyKey_ReturnsExistingOrderWithoutDuplicating()
    {
        // Scenario 6: Duplicate Idempotency-Key header with same payload -> returns existing order without duplicating
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);

        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        OrderItemDto item = CreateItemDto(menuItemIds[0], seatNumber: 1);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: restaurantId,
            items: [item],
            orderType: OrderType.DineIn,
            tableId: tableId);

        // First request with Idempotency-Key
        using HttpRequestMessage request1 = new(HttpMethod.Post, "/v1/orders")
        {
            Content = JsonContent.Create(new { order = dto }, options: JsonOptions),
        };
        request1.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString());

        HttpResponseMessage response1 = await client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created,
            $"body: {await response1.Content.ReadAsStringAsync()}");

        CreateOrderResponse? createResult1 = await response1.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
        createResult1.Should().NotBeNull();
        Guid firstOrderId = createResult1!.Id;

        // Second request with identical Idempotency-Key
        using HttpRequestMessage request2 = new(HttpMethod.Post, "/v1/orders")
        {
            Content = JsonContent.Create(new { order = dto }, options: JsonOptions),
        };
        request2.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString());

        HttpResponseMessage response2 = await client.SendAsync(request2);
        // The endpoint replays the original order via 200 OK
        response2.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await response2.Content.ReadAsStringAsync()}");

        OrderDto? replayDto = await response2.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        replayDto.Should().NotBeNull();
        replayDto!.Id.Should().Be(firstOrderId);

        // Verify in database that only a single order row was created
        using IServiceScope scope = _factory.Services.CreateScope();
        IApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        List<Order> matchingOrders = await dbContext.Orders
            .IgnoreQueryFilters()
            .Where(o => o.RestaurantId == restaurantId && o.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        matchingOrders.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreatePosOrder_MissingTableId_OnDineIn_Returns400BadRequest()
    {
        // Scenario 7: Missing TableId on Dine-In order -> returns 400 Bad Request validation problem details
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateWriteClient(restaurantId);

        var (customerId, menuItemIds) = await SeedOwnerAsync(menuItemCount: 1);
        OrderItemDto item = CreateItemDto(menuItemIds[0], seatNumber: 1);

        OrderDto dto = CreateOrderDto(
            customerId: customerId,
            restaurantId: restaurantId,
            items: [item],
            orderType: OrderType.DineIn,
            tableId: null); // Missing TableId on Dine-In

        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/orders", new { order = dto }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"body: {await response.Content.ReadAsStringAsync()}");

        string responseBody = await response.Content.ReadAsStringAsync();
        responseBody.Should().Contain("TableId");
    }
}
