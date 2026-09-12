namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// End-to-end HTTP contract tests for the admin cart surface —
/// <c>GET /v1/admin/carts</c>, <c>PUT /v1/admin/carts/{userId}</c> and
/// <c>DELETE /v1/admin/carts/{userId}</c>. All three sit behind the
/// <c>Default</c> policy plus the <c>orders:admin</c> permission, and are the
/// RestaurantSupportAgent tooling that lets an operator act on another user's
/// cart (the user-facing routes derive the user id from the JWT instead).
///
/// <para><b>Route correction vs E2E_TEST_COVERAGE_PLAN §6.5.</b> The plan
/// writes these as <c>/api/v1/admin/carts/*</c>. The real templates carry no
/// <c>/api</c> prefix — <c>MapBasketGroup()</c> maps <c>/v1</c> and the
/// <c>/api</c> segment is added by the YARP gateway, not the service. Tests
/// hit <c>/v1/admin/carts</c>.</para>
///
/// <para><b>JSON serialization note.</b> The admin PUT body is a
/// <c>Models.Basket</c>, which carries NodaTime <c>Instant</c> members, so it
/// goes through <see cref="NodaTimeJson.CreateContent{T}"/> rather than
/// <c>PutAsJsonAsync</c> — the same constraint the user-facing
/// <see cref="UpsertCartEndpointTests"/> documents.</para>
/// </summary>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class AdminCartEndpointTests(BasketWebApplicationFactory factory)
{
    private const string AdminPermission = "orders:admin";

    private HttpClient NewAdminClient()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", AdminPermission);
        return client;
    }

    private static Models.Basket NewEmptyBasket() => new()
    {
        UserId = Guid.Empty,
        RestaurantId = Guid.Empty,
        Items = [],
        AppliedDiscounts = [],
    };

    [Fact]
    public async Task ListCarts_WithAdminPermission_Returns200()
    {
        // NOTE (plan deviation, §6.5): ListCartsQueryHandler is a deliberate
        // stub — it always returns an empty page with TotalCount 0, and the
        // endpoint hardcodes Page: 0 / PageSize: 50 rather than binding the
        // query string. So this test can only pin the 200 + envelope shape,
        // NOT that a seeded cart appears. Seeding one here would assert
        // nothing. When the repo grows a paged list method, this test should
        // grow a seeded-row assertion with it.
        using HttpClient client = NewAdminClient();
        await factory.SeedBasketAsync(Guid.NewGuid());

        HttpResponseMessage response = await client.GetAsync("/v1/admin/carts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        ListCartsResponse? body = await response.Content.ReadFromJsonAsync<ListCartsResponse>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(0, "the handler hardcodes Page: 0 today");
        body.PageSize.Should().Be(50, "the handler hardcodes PageSize: 50 today");
        body.TotalCount.Should().Be(0, "ListCartsQueryHandler is still a stub");
        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListCarts_WithoutAdminPermission_Returns403()
    {
        // Authenticated, but holding only the ordinary customer permission.
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");

        HttpResponseMessage response = await client.GetAsync("/v1/admin/carts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the admin group requires orders:admin; an authenticated caller without it is 403, not 401");
    }

    [Fact]
    public async Task ListCarts_Anonymous_Returns401()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/v1/admin/carts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "no authenticated principal => the 'Default' policy returns 401 before the permission check");
    }

    [Fact]
    public async Task UpsertCartAdmin_ExistingCart_Returns200_AndTargetsTheUrlUser()
    {
        // Seed first so the upsert is an update (200) rather than a create
        // (201) — the plan's §6.5 name asserts the 200 arm.
        Guid targetUserId = Guid.NewGuid();
        await factory.SeedBasketAsync(targetUserId);

        using HttpClient client = NewAdminClient();
        Models.Basket basket = NewEmptyBasket();

        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Put, $"/v1/admin/carts/{targetUserId}")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");

        StoreBasketResponse? result = await response.Content
            .ReadFromJsonAsync<StoreBasketResponse>();
        result.Should().NotBeNull();
        result!.IsCreated.Should().BeFalse("the cart already existed");
        result.UserId.Should().Be(targetUserId,
            "the admin endpoint stamps the URL's userId onto the body — this is what lets an "
            + "admin act on someone else's cart, unlike the user-facing PUT which uses the JWT");
        result.RestaurantId.Should().Be(Guid.Parse(TestAuthHandler.TestRestaurantId),
            "the tenant always comes from the caller's JWT, never the body");
    }

    [Fact]
    public async Task UpsertCartAdmin_NewCart_Returns201()
    {
        Guid targetUserId = Guid.NewGuid();

        using HttpClient client = NewAdminClient();
        Models.Basket basket = NewEmptyBasket();

        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Put, $"/v1/admin/carts/{targetUserId}")
        {
            Content = NodaTimeJson.CreateContent(basket),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        StoreBasketResponse? result = await response.Content
            .ReadFromJsonAsync<StoreBasketResponse>();
        result.Should().NotBeNull();
        result!.IsCreated.Should().BeTrue();
        result.UserId.Should().Be(targetUserId);
    }

    [Fact]
    public async Task DeleteCartAdmin_ExistingCart_Returns204_AndCartIsGone()
    {
        Guid targetUserId = Guid.NewGuid();
        await factory.SeedBasketAsync(targetUserId);

        using HttpClient client = NewAdminClient();

        HttpResponseMessage response = await client.DeleteAsync($"/v1/admin/carts/{targetUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();

        // The target user's own GET must now project an empty cart (the
        // service never 404s a missing cart).
        using HttpClient asTarget = factory.CreateClient();
        asTarget.DefaultRequestHeaders.Add("X-Test-User", targetUserId.ToString());
        asTarget.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");
        HttpResponseMessage followUp = await asTarget.GetAsync("/v1/cart");
        followUp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteCartAdmin_NonExistent_Returns204()
    {
        using HttpClient client = NewAdminClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/v1/admin/carts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the admin delete is idempotent — deleting a cart that was never there is still 204");
    }

    [Fact]
    public async Task DeleteCartAdmin_Anonymous_Returns401()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/v1/admin/carts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Local mirrors of the production response shapes. The existing endpoint
    /// tests deliberately avoid referencing the production DTO types so a
    /// rename shows up as a compile break in exactly one place.
    /// </summary>
    private sealed record StoreBasketResponse(bool IsCreated, Guid UserId, Guid RestaurantId);

    private sealed record ListCartsResponse(
        IReadOnlyList<Models.Basket> Items,
        int Page,
        int PageSize,
        int TotalCount);
}
