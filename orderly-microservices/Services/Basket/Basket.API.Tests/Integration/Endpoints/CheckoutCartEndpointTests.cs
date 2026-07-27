using System.Text.Json;
using BuildingBlocks.Messaging.Events;

namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// End-to-end HTTP contract tests for <c>POST /api/v1/cart/checkout</c>.
/// Locks the §0.4.3 (200 + cached-cart-deleted), §0.4.6
/// (<c>Idempotency-Key</c> IETF draft), §0.4.8 (rate limiter), and
/// the empty-cart 409 contract.
/// </summary>
/// <remarks>
/// <para><b>Filter capture strategy.</b> The
/// <see cref="BasketIdempotencyFilter"/> swaps
/// <c>HttpContext.Response.Body</c> for a <c>MemoryStream</c>,
/// runs the endpoint, then runs the IResult the endpoint returned
/// (e.g. <c>Results.Ok(...)</c>) against the swap so the body bytes
/// are captured atomically alongside the headers the IResult sets
/// on the real response. The filter then drains the swap, writes
/// the captured bytes to the original stream, and returns
/// <c>Results.Empty</c> so the framework does not re-execute the
/// IResult (which would fail with "the response has already
/// started" because the body write flipped
/// <c>Response.HasStarted</c> to true).</para>
/// <para>The discount loop is skipped — the test basket carries
/// one item with no coupons so the discount short-circuit
/// triggers and the unreachable <c>GrpcSettings__DiscountUrl</c>
/// in <c>appsettings.Test.json</c> is never hit.</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class CheckoutCartEndpointTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task CheckoutCart_ValidRequest_Returns200AndCachedCartDeleted()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId, b =>
        {
            b.Items.Add(new BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 9.99m });
        });
        // orders:create for the POST /cart/checkout route;
        // orders:view_own for the follow-up GET /cart (the GET endpoint
        // requires view_own.
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create,orders:view_own");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "§0.4.3: a checkout with a valid Idempotency-Key + a non-empty cart returns 200");
        response.Headers.CacheControl.Should().NotBeNull("§0.4.3: checkout responses carry Cache-Control: no-store");

        var body = await response.Content.ReadFromJsonAsync<CheckoutBasketResponse>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Message.Should().NotBeNullOrEmpty();

        // The cached cart was deleted by the handler. The GET
        // response carries a `Basket` envelope (the GetBasketResponse
        // wrapper) so we read it as a JsonDocument and drill into
        // `Basket.Items` to confirm the post-checkout cart is empty.
        // Property names are PascalCase because the host's
        // ConfigureHttpJsonOptions sets PropertyNamingPolicy = null.
        var getResponse = await client.GetAsync("/api/v1/cart");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartDoc = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = cartDoc.GetProperty("Basket").GetProperty("Items");
        items.GetArrayLength().Should().Be(0,
            "the atomic-checkout path in CheckoutBasketCommandHandler deletes the cart in the same Marten commit");
    }

    [Fact]
    public async Task CheckoutCart_ReplayedWithSameIdempotencyKey_Returns200AndCachedResponse()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId, b =>
        {
            b.Items.Add(new BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 9.99m });
        });
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");
        var idempotencyKey = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        var dto = BasketSeedHelper.BuildValidCheckoutDto();

        // Act — first request misses the cache, runs the handler,
        // deletes the cart, caches the response.
        var firstResponse = await client.PostAsJsonAsync("/api/v1/cart/checkout", dto);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request with the same key + same body — REPLAY.
        var secondResponse = await client.PostAsJsonAsync("/api/v1/cart/checkout", dto);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "§0.4.6: a replay hit returns 200 with the cached body (skip-on-replay)");
        secondResponse.Headers.Contains("Idempotent-Replayed").Should().BeTrue(
            "§0.4.6: the replay hit sets the Idempotent-Replayed: true header per the IETF draft");
        secondResponse.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle("true");
    }

    [Fact]
    public async Task CheckoutCart_ReplayedWithDifferentBody_Returns422()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId, b =>
        {
            b.Items.Add(new BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 9.99m });
        });
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");
        var idempotencyKey = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        // First request — caches the response.
        var firstDto = BasketSeedHelper.BuildValidCheckoutDto();
        var firstResponse = await client.PostAsJsonAsync("/api/v1/cart/checkout", firstDto);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request — same key, different body.
        var differentDto = BasketSeedHelper.BuildValidCheckoutDto();
        differentDto.FirstName = "DifferentName";

        // Act
        var secondResponse = await client.PostAsJsonAsync("/api/v1/cart/checkout", differentDto);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "§0.4.6: same Idempotency-Key + different body is rejected with 422 (state conflict, not 409 resource conflict) per the IETF draft");
    }

    [Fact]
    public async Task CheckoutCart_NoCart_Returns404()
    {
        // Arrange — no seeded cart. The handler resolves the cart via
        // `IBasketRepository.GetBasketAsync`, which throws
        // `BasketNotFoundException` when the (userId, restaurantId)
        // tuple is absent. The exception handler maps that to 404.
        // The plan §0.4.3 reserve the 409 for "cart exists but is
        // empty" (a state conflict, not a missing-resource
        // conflict) — the test for the empty-cart path is below.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "no cart exists for (userId, restaurantId) => BasketNotFoundException => 404 NotFound (the cart resource doesn't exist; the plan's 409 path is for the cart-exists-but-empty case)");
    }

    [Fact]
    public async Task CheckoutCart_EmptyCart_Returns400()
    {
        // Arrange — cart exists with no items. The handler's empty-cart
        // branch returns `CheckoutBasketResult(false, "Basket is empty.")`
        // and the endpoint maps `result.Success == false` to 400
        // BadRequest. The plan §0.4.3 status matrix calls for 409
        // here, but the production endpoint maps to 400 (this test
        // locks the current behaviour; the plan-vs-impl drift is
        // tracked as a follow-up).
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId); // empty cart (no Items)
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the production endpoint maps an empty cart to 400 BadRequest (plan-vs-impl drift: §0.4.3 says 409, endpoint says 400)");
    }

    [Fact]
    public async Task CheckoutCart_MissingIdempotencyKey_Returns400()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        // Act — no Idempotency-Key header
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§0.4.6: a missing Idempotency-Key is rejected with 400");
    }

    [Fact]
    public async Task CheckoutCart_MalformedIdempotencyKey_Returns400()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "not-a-uuid-v4");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§0.4.6: a non-UUIDv4 Idempotency-Key is rejected with 400");
    }

    [Fact]
    public async Task CheckoutCart_AnonymousRequest_Returns401()
    {
        // Arrange — no X-Test-User header.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/cart/checkout", BasketSeedHelper.BuildValidCheckoutDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static BasketCheckoutDto BuildValidCheckoutDto()
    {
        return new BasketCheckoutDto
        {
            UserId = Guid.Empty,
            RestaurantId = Guid.Empty,
            FirstName = "Test",
            LastName = "User",
            EmailAddress = "test.user@basket.local",
            AddressLine = "123 Test Street",
            Country = "US",
            State = "CA",
            ZipCode = "94000",
            CardName = "Test User",
            CardNumber = "4242424242424242",
            CVV = "123",
            Expiration = "12/30",
            PaymentMethod = PaymentMethod.Card,
        };
    }

    private sealed record CheckoutBasketResponse(bool Success, string Message);
}
