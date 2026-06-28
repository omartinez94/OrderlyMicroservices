using Identity.API.Features.Roles.UpdateRole;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Roles.UpdateRole;

/// <summary>
/// Covers every branch of <see cref="UpdateRoleCommandHandler"/>: the happy
/// path that updates the three mutable fields, the not-found path, the
/// null-description case, and the case-insensitive re-normalization on rename.
/// </summary>
public sealed class UpdateRoleCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UpdateRoleCommandHandler _sut;

    public UpdateRoleCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new UpdateRoleCommandHandler(_roleManager);
    }

    private async Task<ApplicationRole> SeedRoleAsync(string name = "Manager", string? description = "Initial")
    {
        var role = IdentityTestData.NewRole(name, description);
        var result = await _roleManager.CreateAsync(role);
        result.Succeeded.Should().BeTrue();
        return role;
    }

    /// <summary>
    /// Happy path: every mutable field updates and the response reflects the
    /// new values. Locks in the contract that an admin can rename and
    /// re-describe a role in a single call.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingRole_UpdatesNameAndDescription()
    {
        var role = await SeedRoleAsync();

        var response = await _sut.Handle(
            new UpdateRoleCommand(role.Id, "Senior Manager", "Renamed"),
            CancellationToken.None);

        var stored = _dbContext.Roles.Single();
        stored.Name.Should().Be("Senior Manager");
        stored.NormalizedName.Should().Be("SENIOR MANAGER");
        stored.Description.Should().Be("Renamed");
        response.RoleId.Should().Be(role.Id);
        response.Name.Should().Be("Senior Manager");
        response.Description.Should().Be("Renamed");
    }

    /// <summary>
    /// Role not found → <see cref="NotFoundException"/>. Without this guard,
    /// the handler would silently succeed and return a default response.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownRole_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new UpdateRoleCommand(Guid.NewGuid(), "X", null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Null description is preserved (not coerced to empty string). Locks in
    /// the nullable contract that the admin UI uses to render "no description".
    /// </summary>
    [Fact]
    public async Task Handle_WithNullDescription_StoresNull()
    {
        var role = await SeedRoleAsync(description: "Initial");

        await _sut.Handle(new UpdateRoleCommand(role.Id, "Manager", null), CancellationToken.None);

        _dbContext.Roles.Single().Description.Should().BeNull();
    }
}