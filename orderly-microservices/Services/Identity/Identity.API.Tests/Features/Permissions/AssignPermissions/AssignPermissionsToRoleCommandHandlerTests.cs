using Identity.API.Features.Permissions.AssignPermissions;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Permissions.AssignPermissions;

/// <summary>
/// Covers every branch of <see cref="AssignPermissionsToRoleCommandHandler"/>.
/// This handler is distinct from the same-named one under
/// <c>Roles/AssignPermissions/</c> — both exist independently and target the
/// same junction table, so the tests verify both are correct.
/// </summary>
public sealed class AssignPermissionsToRoleCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AssignPermissionsToRoleCommandHandler _sut;

    public AssignPermissionsToRoleCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new AssignPermissionsToRoleCommandHandler(_roleManager, _dbContext);
    }

    private async Task<ApplicationRole> SeedRoleAsync(string name = "Manager")
    {
        var role = IdentityTestData.NewRole(name);
        await _roleManager.CreateAsync(role);
        return role;
    }

    private async Task<Permission> SeedPermissionAsync(string name = "users:view_all")
    {
        var permission = IdentityTestData.NewPermission(name);
        _dbContext.Permissions.Add(permission);
        await _dbContext.SaveChangesAsync();
        return permission;
    }

    /// <summary>
    /// Happy path: existing role-permission rows are removed and the new set
    /// is written. Same wipe-then-write contract as the Roles-flavored
    /// counterpart.
    /// </summary>
    [Fact]
    public async Task Handle_ReplacesExistingPermissions()
    {
        var role = await SeedRoleAsync();
        var oldPerm = await SeedPermissionAsync("users:view_all");
        _dbContext.RolePermissions.Add(IdentityTestData.NewRolePermission(role, oldPerm));
        await _dbContext.SaveChangesAsync();

        var newPerm = await SeedPermissionAsync("orders:create");
        await _sut.Handle(new AssignPermissionsToRoleCommand(role.Id, [newPerm.Id]), CancellationToken.None);

        var links = _dbContext.RolePermissions.Where(rp => rp.RoleId == role.Id).ToList();
        links.Should().HaveCount(1);
        links.Single().PermissionId.Should().Be(newPerm.Id);
    }

    /// <summary>
    /// Role not found → <see cref="NotFoundException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownRole_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new AssignPermissionsToRoleCommand(Guid.NewGuid(), []), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Unknown permission IDs are silently dropped — same de-facto behavior as
    /// the Roles-flavored counterpart. Locks in the contract that callers get
    /// no signal for which IDs were ignored.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownPermissionIds_DropsThem()
    {
        var role = await SeedRoleAsync();
        var known = await SeedPermissionAsync("users:view_all");
        var orphan = Guid.NewGuid();

        var response = await _sut.Handle(
            new AssignPermissionsToRoleCommand(role.Id, [known.Id, orphan]),
            CancellationToken.None);

        // Response echoes input.
        response.PermissionIds.Should().BeEquivalentTo(new[] { known.Id, orphan });
        // DB only has the known one.
        _dbContext.RolePermissions.Where(rp => rp.RoleId == role.Id)
            .Should().HaveCount(1)
            .And.OnlyContain(rp => rp.PermissionId == known.Id);
    }
}