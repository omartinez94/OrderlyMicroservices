using Identity.API.Features.Roles.AssignPermissions;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Roles.AssignPermissions;

/// <summary>
/// Covers every branch of <see cref="AssignPermissionsCommandHandler"/>: the
/// happy path that wipes and replaces the role's permissions, the not-found
/// path, the empty list case, and the silent-drop of unknown permission IDs.
/// This handler is distinct from the same-named one under
/// <c>Permissions/AssignPermissions/</c> — both exist independently.
/// </summary>
public sealed class AssignPermissionsCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AssignPermissionsCommandHandler _sut;

    public AssignPermissionsCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new AssignPermissionsCommandHandler(_roleManager, _dbContext);
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
    /// is written. Without the wipe, repeated calls would accumulate rows and
    /// eventually violate the (RoleId, PermissionId) unique index.
    /// </summary>
    [Fact]
    public async Task Handle_ReplacesExistingPermissions()
    {
        var role = await SeedRoleAsync();
        var p1 = await SeedPermissionAsync("users:view_all");
        var p2 = await SeedPermissionAsync("orders:create");
        _dbContext.RolePermissions.AddRange(
            IdentityTestData.NewRolePermission(role, p1),
            IdentityTestData.NewRolePermission(role, p2));
        await _dbContext.SaveChangesAsync();

        var p3 = await SeedPermissionAsync("payments:refund");
        var p4 = await SeedPermissionAsync("audit:view");
        await _sut.Handle(new AssignPermissionsCommand(role.Id, [p3.Id, p4.Id]), CancellationToken.None);

        var links = _dbContext.RolePermissions.Where(rp => rp.RoleId == role.Id).ToList();
        links.Select(rp => rp.PermissionId).Should().BeEquivalentTo(new[] { p3.Id, p4.Id });
    }

    /// <summary>
    /// Role not found → <see cref="NotFoundException"/>. Without this guard,
    /// the handler would silently succeed and leave the DB unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownRole_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new AssignPermissionsCommand(Guid.NewGuid(), []), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Empty permission list clears every assignment. Same de-facto "remove
    /// all permissions" affordance as the analogous role/replacement handlers.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyList_RemovesAllPermissions()
    {
        var role = await SeedRoleAsync();
        var p1 = await SeedPermissionAsync();
        _dbContext.RolePermissions.Add(IdentityTestData.NewRolePermission(role, p1));
        await _dbContext.SaveChangesAsync();

        await _sut.Handle(new AssignPermissionsCommand(role.Id, []), CancellationToken.None);

        _dbContext.RolePermissions.Where(rp => rp.RoleId == role.Id).Should().BeEmpty();
    }

    /// <summary>
    /// Permission IDs that don't exist in <c>Permissions</c> are silently
    /// dropped. The handler filters by <c>command.PermissionIds.Contains(p.Id)</c>
    /// against the persisted set, so unknown IDs never produce a row. The
    /// response still echoes the original input list, so the caller doesn't
    /// know which IDs were dropped.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownPermissionIds_SilentlyDropsThem()
    {
        var role = await SeedRoleAsync();
        var p1 = await SeedPermissionAsync("users:view_all");
        var orphanId = Guid.NewGuid();

        var response = await _sut.Handle(
            new AssignPermissionsCommand(role.Id, [p1.Id, orphanId]),
            CancellationToken.None);

        // Response still echoes input — caller has no signal which IDs were dropped.
        response.PermissionIds.Should().BeEquivalentTo(new[] { p1.Id, orphanId });
        // Only the known permission made it into the junction table.
        _dbContext.RolePermissions.Where(rp => rp.RoleId == role.Id)
            .Should().HaveCount(1)
            .And.OnlyContain(rp => rp.PermissionId == p1.Id);
    }
}