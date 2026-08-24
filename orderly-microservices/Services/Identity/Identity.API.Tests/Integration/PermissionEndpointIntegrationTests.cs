using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.API.Data;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.API.Tests.Integration;

/// <summary>
/// HTTP pipeline coverage for <c>/permissions/*</c>. Two endpoints:
/// list and assign-to-role. Both are gated by
/// <c>permissions:view</c> and <c>roles:edit_permissions</c>
/// respectively; the test client injects the matching permission via
/// <c>TestAuthHandler</c>.
/// </summary>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class PermissionEndpointIntegrationTests(IdentityWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

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

    // -------- List --------

    /// <summary>
    /// <c>GET /permissions</c> with <c>permissions:view</c>
    /// returns 200 with the seeded permission catalogue. Production
    /// <c>DataSeeder</c> seeds ~30 baseline permissions; we add one
    /// more here so the test doesn't depend on the exact seed count.
    /// </summary>
    [Fact]
    public async Task ListPermissions_Returns200()
    {
        // Seed an extra permission so the list is non-empty even if
        // the production seed row count shifts in the future.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Name = $"perm-{Guid.NewGuid():N}",
            Resource = "tests",
            Action = "list",
        });
        await db.SaveChangesAsync();

        var client = AuthenticatedClient("permissions:view");

        var response = await client.GetAsync("/v1/permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListPermissionsResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Permissions.Should().NotBeEmpty();
    }

    // -------- Assign to role --------

    /// <summary>
    /// <c>POST /permissions/assign-to-role</c> with
    /// <c>roles:edit_permissions</c> replaces the role's permission
    /// set and returns 200. We seed both a real permission and a real
    /// role so the <c>RolePermissions</c> insert has something to bind.
    /// </summary>
    [Fact]
    public async Task AssignPermissionsToRole_Returns200()
    {
        Guid permissionId;
        Guid roleId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            var permission = new Permission
            {
                Id = Guid.NewGuid(),
                Name = $"perm-{Guid.NewGuid():N}",
                Resource = "tests",
                Action = "assign",
            };
            db.Permissions.Add(permission);

            var role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = $"assign-{Guid.NewGuid():N}",
                NormalizedName = $"assign-{Guid.NewGuid():N}".ToUpperInvariant(),
            };
            var result = await roleManager.CreateAsync(role);
            result.Succeeded.Should().BeTrue();

            await db.SaveChangesAsync();

            permissionId = permission.Id;
            roleId = role.Id;
        }

        var client = AuthenticatedClient("roles:edit_permissions");
        var request = new { RoleId = roleId, PermissionIds = new[] { permissionId } };

        var response = await client.PostAsJsonAsync("/v1/permissions/assign-to-role", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Response shape mirror of <c>Identity.API.Features.Permissions.ListPermissions.ListPermissionsResponse</c>.</summary>
internal record ListPermissionsResponse(IEnumerable<PermissionDto> Permissions);

internal record PermissionDto(Guid Id, string Name, string Description, string Resource, string Action);