namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// End-to-end HTTP contract tests for <c>PUT /api/v1/cart</c>.
/// Locks the §0.4.3 (201 Created on new cart + Location header / 200
/// OK on existing cart), §0.4.10 (spoofing-footgun: body
/// <c>UserId</c> / <c>RestaurantId</c> MUST be <see cref="Guid.Empty"/>
/// — any other value is rejected with 422), and the 400
/// validation path under the §0.4.10 contract.
/// </summary>
/// <remarks>
/// <para>Every PUT uses a fresh <c>userId</c> so the test never
/// collides with a sibling test's seeded cart. The request body
/// always carries <c>UserId = Guid.Empty</c> +
/// <c>RestaurantId = Guid.Empty</c> (the §0.4.10 contract); the
/// endpoint overwrites both with the JWT-derived values before
/// the command is constructed.</para>
/// <para>The discount loop is skipped — the test basket carries
/// <c>AppliedDiscounts: []</c> so the handler short-circuits the
/// <c>Parallel.ForEachAsync</c> gRPC fan-out (the unreachable
/// <c>GrpcSettings__DiscountUrl</c> in <c>appsettings.Test.json</c>
/// would otherwise fail the call). The real-discount path is
/// covered by the existing <c>StoreBasketHandlerTests</c> unit
/// suite.</para>
/// <para><b>JSON serialization note.</b> All PUT
/// requests go through <see cref="NodaTimeJson.CreateContent{T}"/>
/// rather than <c>PutAsJsonAsync</c>. The default
/// <see cref="System.Text.Json.JsonSerializerOptions"/> on
/// <see cref="HttpClient"/> does NOT register the NodaTime
/// converter, so a <c>Basket</c> with <c>Instant</c> properties
/// serializes them as empty objects <c>{}</c> — the host's
/// deserializer then rejects the body with an empty-body 400.
/// <see cref="NodaTimeJson.NodaTimeJsonOptions"/> mirrors the
/// host's <c>ConfigureHttpJsonOptions</c> configuration.</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class UpsertCartEndpointTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task UpsertCart_NewCart_Returns201AndLocation()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        var basket = NewEmptyBasket();

        // Act — NodaTime-aware JSON content; see xmldoc above.
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert
        var responseBodyStr = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"§0.4.3: a new cart PUT returns 201 Created + Location: /api/v1/cart. Response was: {responseBodyStr}");
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().EndWith("/api/v1/cart");

        var body = await response.Content.ReadFromJsonAsync<StoreBasketResponse>();
        body.Should().NotBeNull();
        body!.IsCreated.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertCart_ExistingCart_Returns200()
    {
        // Arrange — seed a basket first.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId);

        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        var basket = NewEmptyBasket();

        // Act
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "§0.4.3: an existing-cart PUT returns 200 OK on every subsequent PUT (idempotent upsert)");

        var body = await response.Content.ReadFromJsonAsync<StoreBasketResponse>();
        body.Should().NotBeNull();
        body!.IsCreated.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertCart_BodyCarriesUserId_OverwrittenByJwt_Returns200()
    {
        // Arrange
        // Spoofing-footgun contract: the endpoint MUST
        // overwrite the body's UserId / RestaurantId with the
        // JWT-derived values BEFORE constructing the command, so the
        // body cannot be used to spoof a different identity. The
        // previous `Equal(Guid.Empty)` validator rule was removed
        // (it ran AFTER the overwrite and saw the JWT values, not the
        // body values — a production bug surfaced by the
        // Phase 5 integration tests). The protection now lives in
        // (a) the endpoint overwrite + (b) the second-layer
        // `BasketIdentityGuardBehavior` cross-check.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        var basket = NewEmptyBasket();
        basket.UserId = Guid.NewGuid(); // spoofing attempt — endpoint overwrites

        // Act
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert — the endpoint overwrites, the request succeeds
        // with the JWT user id, and the response's UserId is the
        // JWT one (not the body's).
        response.StatusCode.Should().Match(c => c == HttpStatusCode.OK || c == HttpStatusCode.Created,
            "the endpoint overwrites the body UserId with the JWT user id; the operation succeeds");

        var body = await response.Content.ReadFromJsonAsync<StoreBasketResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().Be(userId,
            "the endpoint's overwrite wins — the response's UserId is the JWT, not the body's spoofed value");
    }

    [Fact]
    public async Task UpsertCart_BodyCarriesRestaurantId_OverwrittenByJwt_Returns200()
    {
        // Arrange — same contract as the UserId test.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        var basket = NewEmptyBasket();
        basket.RestaurantId = Guid.NewGuid(); // spoofing attempt

        // Act
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Match(c => c == HttpStatusCode.OK || c == HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<StoreBasketResponse>();
        body.Should().NotBeNull();
        body!.RestaurantId.Should().Be(Guid.Parse(TestAuthHandler.TestRestaurantId),
            "the endpoint's overwrite wins — the response's RestaurantId is the JWT's, not the body's");
    }

    [Fact]
    public async Task UpsertCart_AnonymousRequest_Returns401()
    {
        // Arrange — no X-Test-User header.
        using var client = factory.CreateClient();

        var basket = NewEmptyBasket();

        // Act
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "no authenticated principal => 'Default' policy returns 401");
    }

    [Fact]
    public async Task UpsertCart_ValidationError_Returns400()
    {
        // Arrange — basket with an item whose quantity is below the §0.4.10 min.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        var basket = NewEmptyBasket();
        basket.Items.Add(new BasketItem
        {
            MenuItemId = 1,
            Quantity = 0, // Quantity >= 1
            UnitPrice = 1.00m,
        });

        // Act
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "A Quantity < 1 is rejected with 400 by the validator");
    }

    private static Models.Basket NewEmptyBasket() => new()
    {
        // UserId + RestaurantId MUST be Guid.Empty on the
        // wire; the endpoint overwrites both with the JWT-derived
        // values before the command is constructed.
        UserId = Guid.Empty,
        RestaurantId = Guid.Empty,
        Items = [],
        AppliedDiscounts = [],
    };

    private sealed record StoreBasketResponse(bool IsCreated, Guid UserId, Guid RestaurantId);
}
