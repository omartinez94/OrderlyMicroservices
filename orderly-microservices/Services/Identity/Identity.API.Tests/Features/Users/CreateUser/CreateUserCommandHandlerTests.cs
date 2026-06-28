using Identity.API.Features.Users.CreateUser;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.CreateUser;

/// <summary>
/// Covers every branch of <see cref="CreateUserCommandHandler"/>: the empty-payload
/// happy path, the role/restaurant assignment combinations, the default-restaurant
/// selection rule, and the two failure modes (duplicate email, weak password).
/// CreateUser is the admin-driven entry point for new accounts — distinct from
/// self-service Register — so its test surface is broader.
/// </summary>
public sealed class CreateUserCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly CreateUserCommandHandler _sut;

    public CreateUserCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new CreateUserCommandHandler(_userManager, _dbContext);
    }

    private static CreateUserCommand NewCommand(
        string email = "new@test.com",
        string password = "P@ssword1!",
        List<string>? roles = null,
        List<int>? restaurantIds = null,
        int? defaultRestaurantId = null)
        => new(email, password, "Jane", "Doe", null, roles ?? [], restaurantIds ?? [], defaultRestaurantId);

    /// <summary>
    /// Minimal payload (no roles, no restaurants) succeeds, creates the user,
    /// and returns the response. Locks in the contract that the admin endpoint
    /// works without any optional assignment data.
    /// </summary>
    [Fact]
    public async Task Handle_WithMinimalPayload_CreatesUserOnly()
    {
        var response = await _sut.Handle(NewCommand(), CancellationToken.None);

        var stored = _dbContext.Users.Single();
        response.UserId.Should().Be(stored.Id);
        response.Email.Should().Be("new@test.com");
        _dbContext.UserRestaurants.Should().BeEmpty();
        (await _userManager.GetRolesAsync(stored)).Should().BeEmpty();
    }

    /// <summary>
    /// When <c>DefaultRestaurantId</c> is supplied AND matches one of the IDs in
    /// <c>RestaurantIds</c>, exactly that row gets <c>IsDefault = true</c>. The
    /// handler must not interpret the default literally (matching the first list
    /// entry) when the caller named a specific one.
    /// </summary>
    [Fact]
    public async Task Handle_WithExplicitDefaultRestaurant_MarksThatRowDefault()
    {
        await _sut.Handle(NewCommand(restaurantIds: [10, 20, 30], defaultRestaurantId: 20), CancellationToken.None);

        var user = _dbContext.Users.Single();
        _dbContext.UserRestaurants.Where(ur => ur.UserId == user.Id).Should().HaveCount(3);
        _dbContext.UserRestaurants.Single(ur => ur.RestaurantId == 20).IsDefault.Should().BeTrue();
        _dbContext.UserRestaurants.Where(ur => ur.RestaurantId != 20).Should()
            .OnlyContain(ur => ur.IsDefault == false);
    }

    /// <summary>
    /// When <c>DefaultRestaurantId</c> is null, the first <c>RestaurantId</c> in
    /// the list becomes the default. Locks in the fallback rule so a caller who
    /// doesn't care about the default still gets a deterministic single-default
    /// assignment.
    /// </summary>
    [Fact]
    public async Task Handle_WithoutDefaultRestaurant_FirstIdBecomesDefault()
    {
        await _sut.Handle(NewCommand(restaurantIds: [100, 200, 300], defaultRestaurantId: null), CancellationToken.None);

        var user = _dbContext.Users.Single();
        _dbContext.UserRestaurants.Single(ur => ur.RestaurantId == 100).IsDefault.Should().BeTrue();
        _dbContext.UserRestaurants.Where(ur => ur.RestaurantId != 100).Should()
            .OnlyContain(ur => ur.IsDefault == false);
    }

    /// <summary>
    /// Roles are assigned when the list is non-empty. The roles must already
    /// exist in the role store (the handler does not auto-create them).
    /// </summary>
    [Fact]
    public async Task Handle_WithRoles_AssignsThemToUser()
    {
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Waiter"));

        await _sut.Handle(NewCommand(roles: ["Manager", "Waiter"]), CancellationToken.None);

        var user = _dbContext.Users.Single();
        var roles = await _userManager.GetRolesAsync(user);
        roles.Should().BeEquivalentTo(new[] { "Manager", "Waiter" });
    }

    /// <summary>
    /// Duplicate email → <see cref="BadRequestException"/>, no user row
    /// persisted. Same guard as Register, but here it is the admin endpoint's
    /// protection against creating a parallel account to one that already
    /// exists in self-service registration.
    /// </summary>
    [Fact]
    public async Task Handle_WithDuplicateEmail_ThrowsBadRequest()
    {
        await _userManager.CreateAsync(IdentityTestData.NewUser("dup@test.com"), "P@ssword1!");

        var act = () => _sut.Handle(NewCommand(email: "dup@test.com"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("User with this email already exists.");
    }

    /// <summary>
    /// Weak password → UserManager failure → handler throws
    /// <see cref="BadRequestException"/> with every error description joined.
    /// Without this guard, an admin could create accounts with passwords that
    /// wouldn't survive a self-service login flow under the same policy.
    /// </summary>
    [Fact]
    public async Task Handle_WithIdentityErrors_ThrowsBadRequestWithJoinedDescriptions()
    {
        var act = () => _sut.Handle(NewCommand(password: "weak"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.Message.Should().StartWith("User creation failed:");
        ex.Which.Message.Should().Contain("uppercase");
    }
}