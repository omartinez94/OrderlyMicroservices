using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.API.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.API.Tests.Integration;

/// <summary>
/// HTTP pipeline coverage for <c>/api/roles/*</c>. Auth is provided by
/// <c>TestAuthHandler</c> so we can drive the production permission
/// paths. Roles are seeded directly via <see cref="RoleManager{T}"/>
/// because <see cref="IdentityWebApplicationFactory"/> only seeds the
/// production baseline (SuperAdmin / RestaurantAdmin / Manager /
/// etc.) on the very first <c>CreateClient</c>; tests that need a
/// freshly-named role seed their own.
/// </summary>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class RoleEndpointIntegrationTests(IdentityWebApplicationFactory factory)
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

    /// <summary>Inserts a unique role via <see cref="RoleManager{T}"/> and returns its id.</summary>
    private async Task<Guid> SeedRoleAsync(string name, string? description = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
        };
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"SeedRoleAsync('{name}') failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
        return role.Id;
    }

    // -------- Create --------

    /// <summary>
    /// <c>POST /api/roles</c> with <c>roles:create</c> returns 201 and
    /// persists the role via <c>RoleManager.CreateAsync</c>.
    /// </summary>
    [Fact]
    public async Task CreateRole_Returns201()
    {
        var client = AuthenticatedClient("roles:create");
        var request = new { Name = $"role-{Guid.NewGuid():N}", Description = "Test role" };

        var response = await client.PostAsJsonAsync("/api/roles", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    // -------- List --------

    /// <summary>
    /// <c>GET /api/roles</c> with <c>roles:view</c> returns 200 with
    /// the seeded role list. We seed an extra role so the test
    /// doesn't depend on the production seed.
    /// </summary>
    [Fact]
    public async Task ListRoles_Returns200()
    {
        await SeedRoleAsync($"list-{Guid.NewGuid():N}");
        var client = AuthenticatedClient("roles:view");

        var response = await client.GetAsync("/api/roles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListRolesResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Roles.Should().NotBeEmpty();
    }

    // -------- Get by id --------

    /// <summary>
    /// <c>GET /api/roles/{id}</c> with <c>roles:view</c> returns 200
    /// and surfaces the role's permissions via the join-table query
    /// in <c>GetRoleQueryHandler</c>.
    /// </summary>
    [Fact]
    public async Task GetRole_Returns200()
    {
        var roleId = await SeedRoleAsync($"get-{Guid.NewGuid():N}");
        var client = AuthenticatedClient("roles:view");

        var response = await client.GetAsync($"/api/roles/{roleId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------- Update --------

    /// <summary>
    /// <c>PUT /api/roles/{id}</c> with <c>roles:edit</c> returns 200
    /// with the updated name + description. Locks in the
    /// <c>RoleManager.UpdateAsync</c> path is reachable through the
    /// HTTP pipeline.
    /// </summary>
    [Fact]
    public async Task UpdateRole_Returns200()
    {
        var roleId = await SeedRoleAsync($"update-{Guid.NewGuid():N}");
        var client = AuthenticatedClient("roles:edit");
        var request = new { Name = $"updated-{Guid.NewGuid():N}", Description = "Updated" };

        var response = await client.PutAsJsonAsync($"/api/roles/{roleId}", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------- Assign permissions --------

    /// <summary>
    /// <c>PUT /api/roles/{id}/permissions</c> with
    /// <c>roles:edit_permissions</c> replaces the role's permission
    /// set. The endpoint re-queries the permission table so we seed
    /// a real permission row to bind to (rather than fabricating a
    /// Guid that will silently no-op).
    /// </summary>
    [Fact]
    public async Task AssignPermissions_Returns200()
    {
        var roleId = await SeedRoleAsync($"assign-{Guid.NewGuid():N}");

        Guid permissionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var permission = new Permission
            {
                Id = Guid.NewGuid(),
                Name = $"perm-{Guid.NewGuid():N}",
                Resource = "tests",
                Action = "verify",
            };
            db.Permissions.Add(permission);
            await db.SaveChangesAsync();
            permissionId = permission.Id;
        }

        var client = AuthenticatedClient("roles:edit_permissions");
        var request = new { PermissionIds = new[] { permissionId } };

        var response = await client.PutAsJsonAsync($"/api/roles/{roleId}/permissions", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Response shape mirror of <c>Identity.API.Features.Roles.ListRoles.ListRolesResponse</c>.</summary>
internal record ListRolesResponse(IEnumerable<RoleListItem> Roles);

internal record RoleListItem(Guid Id, string Name, string? Description);