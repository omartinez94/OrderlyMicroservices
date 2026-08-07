using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.API.Tests.Integration;

/// <summary>
/// HTTP pipeline coverage for <c>/api/users/*</c>. Auth is provided by
/// <c>TestAuthHandler</c> via the <c>X-Test-User</c> +
/// <c>X-Test-Permissions</c> headers so we can drive the production
/// permission authorization paths without standing up an OAuth client.
/// </summary>
/// <remarks>
/// The user list / get / update / delete endpoints are gated on the
/// <c>users:view_all</c>, <c>users:edit</c>, and <c>users:delete</c>
/// permissions respectively; the helper <see cref="AuthenticatedClient"/>
/// builds a client with the required permission set so individual tests
/// stay focused on the contract under test.
/// </remarks>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class UserEndpointIntegrationTests(IdentityWebApplicationFactory factory)
{
    // Identity.API configures JsonOptions.PropertyNamingPolicy = null, so the
    // server expects PascalCase. The default System.Text.Json behaviour
    // matches that — we deliberately do NOT use JsonSerializerDefaults.Web
    // (which would camelCase the test payloads and fail body binding).
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

    // -------- Create --------

    /// <summary>
    /// <c>POST /api/users</c> with the <c>users:create</c> permission
    /// returns 201 + Location header, persists the user via the real
    /// <c>UserManager</c>, and returns the new <see cref="Guid"/>.
    /// </summary>
    [Fact]
    public async Task CreateUser_Returns201()
    {
        var client = AuthenticatedClient("users:create");
        var request = new
        {
            Email = $"create-{Guid.NewGuid():N}@test.com",
            Password = "P@ssword1!",
            FirstName = "Created",
            LastName = "User",
        };

        var response = await client.PostAsJsonAsync("/api/users", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<CreateUserResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.UserId.Should().NotBe(Guid.Empty);
    }

    // -------- Get --------

    /// <summary>
    /// <c>GET /api/users/{id}</c> with the <c>users:view_all</c>
    /// permission returns 200 with the seeded user's profile fields.
    /// </summary>
    [Fact]
    public async Task GetUser_Returns200()
    {
        var (userId, email) = await factory.SeedUserAsync($"get-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:view_all");

        var response = await client.GetAsync($"/api/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetUserResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Id.Should().Be(userId);
        body.Email.Should().Be(email);
        body.FirstName.Should().NotBeNullOrEmpty();
        body.LastName.Should().NotBeNullOrEmpty();
    }

    // -------- List --------

    /// <summary>
    /// <c>GET /api/users</c> with <c>users:view_all</c> returns 200 with
    /// a paginated envelope. Verifies the production query handler is
    /// wired into the HTTP pipeline (it hasn't been exercised by the
    /// in-memory EF tests).
    /// </summary>
    [Fact]
    public async Task ListUsers_Returns200WithPagination()
    {
        // Seed at least one user so the response has content.
        await factory.SeedUserAsync($"list-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:view_all");

        var response = await client.GetAsync("/api/users?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListUsersResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        body.Users.Should().NotBeEmpty();
    }

    // -------- Update --------

    /// <summary>
    /// <c>PUT /api/users/{id}</c> with <c>users:edit</c> updates the
    /// user's <c>FirstName</c> / <c>LastName</c> and returns 200 with
    /// the updated fields. The <c>UserManager.UpdateAsync</c> path is
    /// the one that owns the audit trail, so this test also locks in
    /// that the HTTP pipeline surfaces the right status.
    /// </summary>
    [Fact]
    public async Task UpdateUser_Returns200()
    {
        var (userId, _) = await factory.SeedUserAsync($"update-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:edit");
        var request = new
        {
            FirstName = "Updated",
            LastName = "Lastname",
            PhoneNumber = "+15550000000",
            IsActive = true,
        };

        var response = await client.PutAsJsonAsync($"/api/users/{userId}", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UpdateUserResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.FirstName.Should().Be("Updated");
        body.LastName.Should().Be("Lastname");
    }

    // -------- Delete --------

    /// <summary>
    /// <c>DELETE /api/users/{id}</c> with <c>users:delete</c> returns
    /// 204 and removes the user row. The endpoint uses the soft-delete
    /// pattern via <c>UserManager.DeleteAsync</c>; the row count drop
    /// is observable on a follow-up GET in the authorization
    /// enforcement suite.
    /// </summary>
    [Fact]
    public async Task DeleteUser_Returns204()
    {
        var (userId, _) = await factory.SeedUserAsync($"delete-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:delete");

        var response = await client.DeleteAsync($"/api/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // -------- Assign roles --------

    /// <summary>
    /// <c>PUT /api/users/{id}/roles</c> with
    /// <c>users:assign_roles</c> replaces the user's role set and
    /// returns 200 with the new role names. The handler uses
    /// <c>UserManager.RemoveFromRolesAsync</c> +
    /// <c>AddToRolesAsync</c>; we only assert the contract, not the
    /// internal call order (covered by the unit test).
    /// </summary>
    [Fact]
    public async Task AssignRoles_Returns200()
    {
        var (userId, _) = await factory.SeedUserAsync($"roles-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:assign_roles");
        var request = new { Roles = new[] { "Manager" } };

        var response = await client.PutAsJsonAsync($"/api/users/{userId}/roles", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AssignRolesResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Roles.Should().ContainSingle().Which.Should().Be("Manager");
    }

    // -------- Assign restaurants --------

    /// <summary>
    /// <c>PUT /api/users/{id}/restaurants</c> with
    /// <c>users:assign_restaurants</c> replaces the user's restaurant
    /// assignments and returns 200 with the new shape. Uses Guid
    /// restaurant ids (post-Phase-5 schema) so the
    /// <c>UserRestaurant</c> insert hits a real foreign-key-less row.
    /// </summary>
    [Fact]
    public async Task AssignRestaurants_Returns200()
    {
        var (userId, _) = await factory.SeedUserAsync($"rest-{Guid.NewGuid():N}@test.com");
        var client = AuthenticatedClient("users:assign_restaurants");
        var request = new
        {
            Restaurants = new[]
            {
                new { RestaurantId = Guid.NewGuid(), IsDefault = true },
            },
        };

        var response = await client.PutAsJsonAsync(
            $"/api/users/{userId}/restaurants", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AssignRestaurantsResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Restaurants.Should().ContainSingle();
    }
}

/// <summary>
/// Response shape mirror of <c>Identity.API.Features.Users.CreateUser.CreateUserResponse</c>.
/// Repeated here because the production record is internal to the
/// feature folder and the test only needs the public surface.
/// </summary>
internal record CreateUserResponse(Guid UserId, string Email, string FirstName, string LastName);

/// <summary>Response shape mirror of <c>Identity.API.Features.Users.GetUser.GetUserResponse</c>.</summary>
internal record GetUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    List<string> Roles,
    List<object> Restaurants);

/// <summary>Response shape mirror of <c>Identity.API.Features.Users.ListUsers.ListUsersResponse</c>.</summary>
internal record ListUsersResponse(IEnumerable<object> Users, int TotalCount);

/// <summary>Response shape mirror of <c>Identity.API.Features.Users.UpdateUser.UpdateUserResponse</c>.</summary>
internal record UpdateUserResponse(Guid UserId, string Email, string FirstName, string LastName, bool IsActive);

/// <summary>Response shape mirror of <c>Identity.API.Features.Users.AssignRoles.AssignRolesResponse</c>.</summary>
internal record AssignRolesResponse(Guid UserId, List<string> Roles);

/// <summary>Response shape mirror of <c>Identity.API.Features.Users.AssignRestaurants.AssignRestaurantsResponse</c>.</summary>
internal record AssignRestaurantsResponse(Guid UserId, List<object> Restaurants);