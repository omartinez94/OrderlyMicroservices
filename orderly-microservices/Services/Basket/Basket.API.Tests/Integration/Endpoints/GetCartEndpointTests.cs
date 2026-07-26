namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// End-to-end HTTP contract tests for <c>GET /api/v1/cart</c>.
/// Locks the §0.4.3 (200 / 304) and §0.4.7 (empty-cart returns 200,
/// never 404) contracts over the real ASP.NET Core pipeline:
/// Carter → MapBasketGroup → MediatR → BasketIdentityGuardBehavior →
/// GetBasketHandler → CachedBasketRepository → Marten → Postgres.
/// </summary>
/// <remarks>
/// <para>Every test seeds baskets via
/// <see cref="BasketSeedHelper.SeedBasketAsync"/> using the
/// tenant-scoped <c>IDocumentStore.LightweightSession(TestRestaurantId)</c>
/// — the test's auth scheme (<see cref="TestAuthHandler"/>) stamps
/// the same <c>restaurantId</c> claim on the principal, so the
/// Marten <c>MultiTenanted()</c> filter lines up the read with
/// the write.</para>
/// <para>The <c>X-Test-User</c> header carries a fresh Guid per test
/// so the seeded cart is the only one in the tenant partition
/// (the Postgres container is fresh per fixture build but the
/// tenant partition may carry rows from prior tests if the
/// per-test cleanup is wrong).</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class GetCartEndpointTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task GetCart_NoCartYet_Returns200WithEmptyBody()
    {
        // Arrange — fresh user, no seeded cart.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");

        // Act
        var response = await client.GetAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "§0.4.7: GET /api/v1/cart for a user with no active cart returns 200 + empty body, never 404");

        response.Headers.CacheControl.Should().NotBeNull(
            "every cart endpoint must set Cache-Control: no-store (PII)");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<CartResponse>();
        body.Should().NotBeNull();
        body!.Basket.Items.Should().BeEmpty();
        body.Basket.TotalItems.Should().Be(0);
        body.Basket.Subtotal.Should().Be(0m);
    }

    [Fact]
    public async Task GetCart_WithCart_Returns200AndBody()
    {
        // Arrange — seed a basket with one item under the test tenant.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var seeded = await factory.SeedBasketAsync(userId, b =>
        {
            b.Items.Add(new BasketItem
            {
                MenuItemId = 42,
                Quantity = 2,
                UnitPrice = 9.99m,
            });
        });

        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");

        // Act
        var response = await client.GetAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CartResponse>();
        body.Should().NotBeNull();
        body!.Basket.Items.Should().HaveCount(1);
        body.Basket.Items[0].MenuItemId.Should().Be(42);
        body.Basket.Items[0].Quantity.Should().Be(2);
        body.Basket.Subtotal.Should().Be(19.98m);
    }

    [Fact]
    public async Task GetCart_AnonymousRequest_Returns401()
    {
        // Arrange — no X-Test-User header.
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "no authenticated principal => the 'Default' policy + MapBasketGroup.RequireAuthorization returns 401");
    }

    [Fact]
    public async Task GetCart_ConditionalGetWithMatchingETag_Returns304()
    {
        // Arrange — seed a basket + first GET captures the ETag.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId, b =>
        {
            b.Items.Add(new BasketItem
            {
                MenuItemId = 7,
                Quantity = 1,
                UnitPrice = 5.00m,
            });
        });

        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");

        var firstResponse = await client.GetAsync("/api/v1/cart");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = firstResponse.Headers.ETag;
        etag.Should().NotBeNull("the GET endpoint must set an ETag on a populated cart");
        // ASP.NET Core's IHeaderDictionary.IfNoneMatch getter parses the
        // raw header into EntityTagHeaderValue instances, which REQUIRES
        // the value to be a quoted-string (RFC 9110 §8.8.3). Send the
        // ETag with its surrounding double-quotes.
        var etagValue = etag!.Tag;

        // Act — second GET with If-None-Match
        using var second = factory.CreateClient();
        second.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        second.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");
        second.DefaultRequestHeaders.Add("If-None-Match", etagValue);

        var secondResponse = await second.GetAsync("/api/v1/cart");

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotModified,
            "matching ETag + If-None-Match => 304 with no body");
        var body = await secondResponse.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
    }

    /// <summary>
    /// Loose shape for the GET response. The endpoint projects
    /// <c>Models.Basket</c> through Mapster into <c>GetBasketResponse</c>;
    /// we only assert the fields the §0.4.7 contract locks.
    /// </summary>
    private sealed record CartResponse(CartBody Basket);

    private sealed record CartBody(
        List<BasketItem> Items,
        decimal Subtotal,
        decimal DiscountAmount,
        decimal Total,
        int TotalItems);
}
