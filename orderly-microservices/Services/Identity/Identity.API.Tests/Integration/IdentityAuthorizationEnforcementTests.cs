using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.API.Tests.Integration;

/// <summary>
/// Per-endpoint authorization coverage for the protected Identity.API
/// write endpoints. Each protected endpoint must:
/// </summary>
/// <list type="bullet">
/// <item>return <see cref="HttpStatusCode.Unauthorized"/> (401) when
/// the request has no <c>X-Test-User</c> header — the production
/// OpenIddict validation scheme behaves the same way on a missing
/// bearer token.</item>
/// <item>return <see cref="HttpStatusCode.Forbidden"/> (403) when the
/// request has a user but lacks the required permission.</item>
/// </list>
/// <remarks>
/// <para>The chosen protected endpoints span every permissioned
/// resource — Users, Roles, Permissions, AuditLog — so a regression
/// in <see cref="BuildingBlocks.Authorization.EndpointRouteBuilderExtensions.RequirePermission"/>
/// surfaces here, not in a single suite.</para>
/// <para>Read endpoints (<c>GET /auth/login</c>, <c>POST /auth/register</c>)
/// intentionally remain anonymous and are covered by
/// <see cref="AuthFlowIntegrationTests"/>.</para>
/// </remarks>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class IdentityAuthorizationEnforcementTests(IdentityWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private HttpClient AnonymousClient() => factory.CreateClient();

    private HttpClient AuthenticatedClient(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Test-Permissions", string.Join(",", permissions));
        }
        return client;
    }

    // -------- Users --------

    [Fact]
    public async Task CreateUser_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/v1/users",
            new { Email = "x@y.com", Password = "P@ssword1!", FirstName = "x", LastName = "y" },
            JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUser_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync($"/v1/users/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteUser_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.DeleteAsync($"/v1/users/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_WrongPermission_Returns403()
    {
        // Have a user identity but only an unrelated permission; the
        // RequirePermission policy should refuse with 403, not 401.
        var client = AuthenticatedClient("audit:view");
        var resp = await client.PostAsJsonAsync("/v1/users",
            new { Email = "wrong@y.com", Password = "P@ssword1!", FirstName = "x", LastName = "y" },
            JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------- Roles --------

    [Fact]
    public async Task CreateRole_WrongPermission_Returns403()
    {
        var client = AuthenticatedClient("users:view_all");
        var resp = await client.PostAsJsonAsync("/v1/roles",
            new { Name = "wrong-perm-role" }, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRoles_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/v1/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- Permissions --------

    [Fact]
    public async Task ListPermissions_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/v1/permissions");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- Audit log --------

    [Fact]
    public async Task GetAuditLog_NoAuth_Returns401()
    {
        var client = AnonymousClient();
        var resp = await client.GetAsync("/v1/audit-log");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLog_WrongPermission_Returns403()
    {
        var client = AuthenticatedClient("users:view_all");
        var resp = await client.GetAsync("/v1/audit-log");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}