using Identity.API.Features.Users.AssignRoles;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.AssignRoles;

/// <summary>
/// Covers every branch of <see cref="AssignRolesCommandHandler"/>: the
/// replace-existing-roles happy path, the not-found path, the empty list case
/// (which is how you remove every role from a user), and the response-echoes-
/// input contract. The handler treats the command's role list as the source of
/// truth, so this last contract is what callers rely on.
/// </summary>
public sealed class AssignRolesCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AssignRolesCommandHandler _sut;

    public AssignRolesCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new AssignRolesCommandHandler(_userManager, _dbContext);
    }

    private async Task<ApplicationUser> SeedUserInRolesAsync(params string[] roleNames)
    {
        var user = IdentityTestData.NewUser();
        await _userManager.CreateAsync(user, "P@ssword1!");
        foreach (var name in roleNames)
        {
            await _roleManager.CreateAsync(IdentityTestData.NewRole(name));
        }
        if (roleNames.Length > 0)
        {
            await _userManager.AddToRolesAsync(user, roleNames);
        }
        return user;
    }

    /// <summary>
    /// Replacing roles: the user had <c>["OldRole"]</c>, the command assigns
    /// <c>["NewRole1", "NewRole2"]</c>, and the resulting role set is exactly
    /// the new one. This is the contract that lets an admin promote/demote a
    /// user in a single call without writing role-delta logic.
    /// </summary>
    [Fact]
    public async Task Handle_ReplacesExistingRoles()
    {
        var user = await SeedUserInRolesAsync("OldRole");
        await _roleManager.CreateAsync(IdentityTestData.NewRole("NewRole1"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("NewRole2"));

        await _sut.Handle(new AssignRolesCommand(user.Id, ["NewRole1", "NewRole2"]), CancellationToken.None);

        var roles = await _userManager.GetRolesAsync(user);
        roles.Should().BeEquivalentTo(new[] { "NewRole1", "NewRole2" });
    }

    /// <summary>
    /// User not found → <see cref="NotFoundException"/>. Without this guard,
    /// a typo in the user id would silently succeed and the (non-existent)
    /// user would appear to have been updated in the response.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new AssignRolesCommand(Guid.NewGuid(), ["Manager"]), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Empty role list removes every existing role. This is the de-facto
    /// "unassign all roles" affordance — without it, admin tooling would need
    /// to invent its own mechanism.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyRoleList_RemovesAllCurrentRoles()
    {
        var user = await SeedUserInRolesAsync("OldRole1", "OldRole2");

        await _sut.Handle(new AssignRolesCommand(user.Id, []), CancellationToken.None);

        var roles = await _userManager.GetRolesAsync(user);
        roles.Should().BeEmpty();
    }

    /// <summary>
    /// The response echoes the requested roles. The handler does not
    /// re-read the role store after assignment — it returns the command's
    /// role list verbatim — so the caller sees exactly what they sent. This
    /// is a deliberate "trust the caller" contract: <c>AddToRolesAsync</c>
    /// will throw if a role name is unknown, so by the time the response is
    /// constructed the assignment has succeeded.
    /// </summary>
    [Fact]
    public async Task Handle_ResponseEchoesRequestedRoles()
    {
        var user = await SeedUserInRolesAsync(); // no initial roles
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Waiter"));

        var response = await _sut.Handle(new AssignRolesCommand(user.Id, ["Manager", "Waiter"]), CancellationToken.None);

        response.Roles.Should().BeEquivalentTo(new[] { "Manager", "Waiter" });
    }
}