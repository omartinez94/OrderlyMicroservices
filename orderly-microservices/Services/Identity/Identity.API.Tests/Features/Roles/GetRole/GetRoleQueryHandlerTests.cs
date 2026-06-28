using Identity.API.Features.Roles.GetRole;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Roles.GetRole;

/// <summary>
/// Covers every branch of <see cref="GetRoleQueryHandler"/>: the happy path
/// that joins role-permissions into <c>PermissionDto</c>, the not-found path,
/// and the "role exists but has no permissions" case.
/// </summary>
public sealed class GetRoleQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly GetRoleQueryHandler _sut;

    public GetRoleQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new GetRoleQueryHandler(_roleManager, _dbContext);
    }

    /// <summary>
    /// Happy path: the role is returned with its permissions joined and
    /// projected into <see cref="PermissionDto"/>. Locks in the response shape
    /// that the role-edit view consumes.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingRole_ReturnsRoleAndPermissions()
    {
        var role = IdentityTestData.NewRole("Manager", "Day-to-day");
        await _roleManager.CreateAsync(role);
        var p1 = IdentityTestData.NewPermission("users:view_all", description: "View all users");
        var p2 = IdentityTestData.NewPermission("orders:create", description: "Create orders");
        _dbContext.Permissions.AddRange(p1, p2);
        _dbContext.RolePermissions.AddRange(
            IdentityTestData.NewRolePermission(role, p1),
            IdentityTestData.NewRolePermission(role, p2));
        await _dbContext.SaveChangesAsync();

        var response = await _sut.Handle(new GetRoleQuery(role.Id), CancellationToken.None);

        response.Id.Should().Be(role.Id);
        response.Name.Should().Be("Manager");
        response.Description.Should().Be("Day-to-day");
        response.Permissions.Should().HaveCount(2);
        response.Permissions.Select(p => p.Name).Should().BeEquivalentTo(new[] { "users:view_all", "orders:create" });
        response.Permissions.First().Description.Should().Be("View all users");
    }

    /// <summary>
    /// Role not found → <see cref="NotFoundException"/>. Without this guard,
    /// the handler would return a default response and the caller would treat
    /// it as a successful fetch of a (non-existent) role.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownRole_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new GetRoleQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Role exists but has no permission assignments → empty list, not null.
    /// The empty-collection contract is what the JSON serializer and the UI
    /// depend on; a null would break both.
    /// </summary>
    [Fact]
    public async Task Handle_WithRoleWithoutPermissions_ReturnsEmptyPermissionsList()
    {
        var role = IdentityTestData.NewRole("Empty");
        await _roleManager.CreateAsync(role);

        var response = await _sut.Handle(new GetRoleQuery(role.Id), CancellationToken.None);

        response.Id.Should().Be(role.Id);
        response.Permissions.Should().NotBeNull().And.BeEmpty();
    }
}