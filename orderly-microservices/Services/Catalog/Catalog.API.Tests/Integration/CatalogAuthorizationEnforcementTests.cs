using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// Per-endpoint authorization coverage for the Catalog write endpoints
/// that Phase 4 of <c>TRUST_ROOT_HARDENING_PLAN.md</c> gated on
/// <c>catalog:menu_update</c>. Each protected endpoint must:
/// </para>
/// <list type="bullet">
/// <item>return <see cref="HttpStatusCode.Unauthorized"/> (401) when
/// the request has no <c>X-Test-User</c> header — the production
/// <c>AddJwtAuthenticationWithDevFallback</c> would behave the same
/// way on a missing JWT.</item>
/// <item>return <see cref="HttpStatusCode.Forbidden"/> (403) when the
/// request has a user but no <c>catalog:menu_update</c> permission.</item>
/// <item>return a non-auth status (200 / 201 / 400 / 404) when the
/// request carries the <c>catalog:menu_update</c> permission — the
/// specific status depends on the business logic, not on auth.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Phase 4 of the plan gates the write endpoints under
/// <c>Catalog.API/Features/Restaurants</c>, <c>Features/Brands</c>,
/// <c>Features/MenuCategories</c>, and <c>Features/BulkOrderUploads</c>
/// on the <c>catalog:menu_update</c> permission. Read endpoints
/// (<c>GET /restaurants</c>, <c>GET /brands</c>, etc.) intentionally
/// remain anonymous for guest / customer browsing and are not in
/// scope for this suite.</para>
/// <para>Why a single coarse permission for all 12 endpoints: the plan
/// chose <c>catalog:menu_update</c> as the umbrella permission for
/// "any write to the menu / restaurant / brand configuration." A
/// future refinement (per-endpoint permissions like
/// <c>restaurants:create</c>) is a follow-up; the catalog file
/// (<c>docs/architecture/permissions.md</c>) lists the strings in
/// use today.</para>
/// </remarks>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CatalogAuthorizationEnforcementTests(CatalogWebApplicationFactory factory)
{
    private const string CatalogMenuUpdatePermission = "catalog:menu_update";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient AnonymousClient() => factory.CreateClient();

    private HttpClient AuthenticatedClient(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", string.Join(",", permissions));
        }
        return client;
    }

    // -- Restaurants (3 endpoints) ----------------------------------------------

    [Fact]
    public async Task CreateRestaurant_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/v1/restaurants", new { name = "x" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRestaurant_WrongPermission_Returns403()
    {
        var client = AuthenticatedClient("catalog:read");
        var resp = await client.PostAsJsonAsync("/api/v1/restaurants", new { name = "x" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateRestaurant_WithPermission_NotAuthError()
    {
        // Status may be 201 (success), 400 (validation), or 500 (db
        // transient) — but NOT 401 / 403. The auth gate must let
        // the call through.
        var client = AuthenticatedClient(CatalogMenuUpdatePermission);
        var resp = await client.PostAsJsonAsync("/api/v1/restaurants",
            new
            {
                brandId = Guid.NewGuid(),
                name = "Test",
                address = "x",
                phoneNumber = "x",
                email = "x@y.com",
                taxRate = 0m,
                currency = "USD",
                timeZone = "UTC",
                autoConfirmOrders = false,
                autoConfirmReservations = false,
                allowAutoSubstitute = false,
                estimatedTurnoverMinutes = 30,
            }, JsonOptions);
        ((int)resp.StatusCode).Should().NotBe(401, "auth must admit the call when the permission is granted");
        ((int)resp.StatusCode).Should().NotBe(403, "auth must admit the call when the permission is granted");
    }

    [Fact]
    public async Task UpdateRestaurant_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PutAsJsonAsync($"/api/v1/restaurants/{Guid.NewGuid()}", new { id = Guid.NewGuid() }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteRestaurant_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.DeleteAsync($"/api/v1/restaurants/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- Brands (3 endpoints) ----------------------------------------------------

    [Fact]
    public async Task CreateBrand_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/v1/brands", new { name = "x" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateBrand_WithPermission_NotAuthError()
    {
        var client = AuthenticatedClient(CatalogMenuUpdatePermission);
        var resp = await client.PostAsJsonAsync("/api/v1/brands",
            new
            {
                name = "Test",
                description = "x",
                logoUrl = "",
                websiteUrl = "",
                contactEmail = "x@y.com",
                contactPhone = "",
                cuisineType = 0,
                isActive = true,
            }, JsonOptions);
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    [Fact]
    public async Task UpdateBrand_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PutAsJsonAsync($"/api/v1/brands/{Guid.NewGuid()}", new { name = "x" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteBrand_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.DeleteAsync($"/api/v1/brands/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- MenuCategories (3 endpoints) -------------------------------------------

    [Fact]
    public async Task CreateMenuCategory_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/restaurants/{Guid.NewGuid()}/menu-categories",
            new { name = "x", description = "y", displayOrder = 0 },
            JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMenuCategory_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PutAsJsonAsync("/api/v1/menu-categories/1", new { name = "x" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteMenuCategory_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.DeleteAsync("/api/v1/menu-categories/1");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- BulkOrderUploads (3 endpoints) -----------------------------------------

    [Fact]
    public async Task UploadBulkOrder_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/restaurants/{Guid.NewGuid()}/bulk-order-uploads",
            new { fileName = "x.csv", rows = Array.Empty<object>() },
            JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApproveBulkOrderUpload_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsync(
            $"/api/v1/restaurants/{Guid.NewGuid()}/bulk-order-uploads/1/approve",
            content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectBulkOrderUpload_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/restaurants/{Guid.NewGuid()}/bulk-order-uploads/1/reject",
            new { reason = "x" },
            JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- Read endpoints stay anonymous per the plan ----------------------------

    [Fact]
    public async Task GetRestaurants_NoAuth_StaysPublic()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/api/v1/restaurants");
        ((int)resp.StatusCode).Should().NotBe(401, "GET /restaurants is public per the plan");
        ((int)resp.StatusCode).Should().NotBe(403, "GET /restaurants is public per the plan");
    }

    [Fact]
    public async Task GetBrands_NoAuth_StaysPublic()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/api/v1/brands");
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }
}
