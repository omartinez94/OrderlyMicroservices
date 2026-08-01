using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Per-endpoint authorization coverage for the 6 Ordering endpoints
/// that Phase 4 of <c>TRUST_ROOT_HARDENING_PLAN.md</c> gated on
/// <c>orders:write</c> (mutating endpoints) or
/// <c>orders:view_own</c> (read endpoints). The asserted behaviour
/// mirrors the Catalog suite: 401 on no auth, 403 on wrong permission,
/// non-auth status on the right permission.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>Catalog.API.Tests/Integration/CatalogAuthorizationEnforcementTests</c>.
/// Ordering also has the existing <c>ConfirmOrder</c> endpoint gated
/// on <c>kitchen:update_prep_status</c> — that's already covered by
/// the existing test suite and predates Phase 4, so it's not in this
/// file's scope.</para>
/// </remarks>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingAuthorizationEnforcementTests(OrderingWebApplicationFactory factory)
{
    private const string OrdersWritePermission = "orders:write";
    private const string OrdersViewOwnPermission = "orders:view_own";

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

    // -- orders:write (Create / Update / Delete) --------------------------------

    [Fact]
    public async Task CreateOrder_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/v1/orders", new { order = new { } }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOrder_WrongPermission_Returns403()
    {
        // Caller has the read permission but not the write permission.
        var client = AuthenticatedClient(OrdersViewOwnPermission);
        var resp = await client.PostAsJsonAsync("/api/v1/orders", new { order = new { } }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateOrder_WithPermission_NotAuthError()
    {
        // Status may be 201, 400, or 500 — but NOT 401 / 403.
        var client = AuthenticatedClient(OrdersWritePermission);
        var resp = await client.PostAsJsonAsync("/api/v1/orders", new { order = new { } }, JsonOptions);
        ((int)resp.StatusCode).Should().NotBe(401, "auth must admit the call when orders:write is granted");
        ((int)resp.StatusCode).Should().NotBe(403, "auth must admit the call when orders:write is granted");
    }

    [Fact]
    public async Task UpdateOrder_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PutAsJsonAsync("/api/v1/orders", new { order = new { } }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateOrder_WithPermission_NotAuthError()
    {
        var client = AuthenticatedClient(OrdersWritePermission);
        var resp = await client.PutAsJsonAsync("/api/v1/orders", new { order = new { } }, JsonOptions);
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    [Fact]
    public async Task DeleteOrder_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.DeleteAsync($"/api/v1/orders/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- orders:view_own (GetOrders / GetOrderById / GetOrdersByCustomer) -------

    [Fact]
    public async Task GetOrders_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/api/v1/orders");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrders_WithPermission_NotAuthError()
    {
        var client = AuthenticatedClient(OrdersViewOwnPermission);
        var resp = await client.GetAsync("/api/v1/orders");
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    [Fact]
    public async Task GetOrderById_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrdersByCustomer_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync($"/api/v1/orders/customer/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrderById_WithPermission_NotAuthError()
    {
        var client = AuthenticatedClient(OrdersViewOwnPermission);
        var resp = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    [Fact]
    public async Task GetOrdersByCustomer_WithPermission_NotAuthError()
    {
        var client = AuthenticatedClient(OrdersViewOwnPermission);
        var resp = await client.GetAsync($"/api/v1/orders/customer/{Guid.NewGuid()}");
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    // -- Cross-permission check: orders:write does NOT leak into reads ---------

    [Fact]
    public async Task GetOrders_WriteOnly_Still403()
    {
        // The write permission is more powerful but does not include
        // the read permission — a write-only token must NOT be able
        // to list orders. The two permissions are independent.
        var client = AuthenticatedClient(OrdersWritePermission);
        var resp = await client.GetAsync("/api/v1/orders");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "orders:write and orders:view_own are independent permissions");
    }
}
