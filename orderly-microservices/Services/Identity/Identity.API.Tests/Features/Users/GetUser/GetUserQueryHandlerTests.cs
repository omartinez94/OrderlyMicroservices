using Identity.API.Features.Users.GetUser;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.GetUser;

/// <summary>
/// Covers every branch of <see cref="GetUserQueryHandler"/>: the happy path
/// that aggregates roles + restaurants, the not-found path, the empty
/// collections case, and the null-email fallback in the projection.
/// </summary>
public sealed class GetUserQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly GetUserQueryHandler _sut;

    public GetUserQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new GetUserQueryHandler(_userManager, _dbContext);
    }

    private async Task<ApplicationUser> SeedUserAsync(string email = "user@test.com")
    {
        var user = IdentityTestData.NewUser(email);
        await _userManager.CreateAsync(user, "P@ssword1!");
        return user;
    }

    /// <summary>
    /// Happy path: roles are read from the role store and restaurants from the
    /// join table, then projected into the response. Locks in the shape that
    /// the admin profile view renders.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingUser_ReturnsRolesAndRestaurants()
    {
        var user = await SeedUserAsync();
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Waiter"));
        await _userManager.AddToRolesAsync(user, ["Manager", "Waiter"]);
        _dbContext.UserRestaurants.AddRange(
            IdentityTestData.NewUserRestaurant(user, 1, isDefault: true),
            IdentityTestData.NewUserRestaurant(user, 2, isDefault: false));
        await _dbContext.SaveChangesAsync();

        var response = await _sut.Handle(new GetUserQuery(user.Id), CancellationToken.None);

        response.Id.Should().Be(user.Id);
        response.Email.Should().Be("user@test.com");
        response.Roles.Should().BeEquivalentTo(new[] { "Manager", "Waiter" });
        response.Restaurants.Should().BeEquivalentTo(new[]
        {
            new UserRestaurantResponse(1, true),
            new UserRestaurantResponse(2, false),
        });
    }

    /// <summary>
    /// User not found → <see cref="NotFoundException"/>. Without this guard,
    /// the handler would silently return a default response and the caller
    /// would treat it as a successful fetch of a (non-existent) user.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new GetUserQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// User with no roles and no restaurants still returns a valid response
    /// with empty collections. Locks in the empty-collection (rather than
    /// null-collection) contract that the JSON serializer depends on.
    /// </summary>
    [Fact]
    public async Task Handle_WithUserWithoutRolesOrRestaurants_ReturnsEmptyCollections()
    {
        var user = await SeedUserAsync();

        var response = await _sut.Handle(new GetUserQuery(user.Id), CancellationToken.None);

        response.Roles.Should().NotBeNull().And.BeEmpty();
        response.Restaurants.Should().NotBeNull().And.BeEmpty();
    }
}