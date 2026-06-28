using Identity.API.Features.Roles.CreateRole;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Roles.CreateRole;

/// <summary>
/// Covers every branch of <see cref="CreateRoleCommandHandler"/>: the happy path
/// (with and without a description), the duplicate-name guard, and the
/// case-insensitive normalization of <c>NormalizedName</c>. Roles are the
/// authorization primitive; a regression here directly affects who-can-do-what.
/// </summary>
public sealed class CreateRoleCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly CreateRoleCommandHandler _sut;

    public CreateRoleCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new CreateRoleCommandHandler(_roleManager);
    }

    /// <summary>
    /// Happy path with a description: the role is created with every field
    /// populated and the response reflects them. Locks in the response shape
    /// that the role-creation endpoint returns.
    /// </summary>
    [Fact]
    public async Task Handle_WithDescription_CreatesRoleAndReturnsResponse()
    {
        var response = await _sut.Handle(
            new CreateRoleCommand("Manager", "Day-to-day operations"),
            CancellationToken.None);

        var stored = _dbContext.Roles.Single();
        response.RoleId.Should().Be(stored.Id);
        response.Name.Should().Be("Manager");
        response.Description.Should().Be("Day-to-day operations");
        stored.NormalizedName.Should().Be("MANAGER");
    }

    /// <summary>
    /// Null description is preserved. Locks in the nullable-description
    /// contract that the admin UI relies on when showing roles without a
    /// description.
    /// </summary>
    [Fact]
    public async Task Handle_WithNullDescription_StoresNull()
    {
        await _sut.Handle(new CreateRoleCommand("Waiter", null), CancellationToken.None);

        _dbContext.Roles.Single().Description.Should().BeNull();
    }

    /// <summary>
    /// Duplicate name → <see cref="BadRequestException"/>. The guard uses
    /// <c>FindByNameAsync</c>, which is case-insensitive (normalized lookup),
    /// so <c>"manager"</c> and <c>"MANAGER"</c> both collide with <c>"Manager"</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WithDuplicateName_ThrowsBadRequest()
    {
        await _sut.Handle(new CreateRoleCommand("Manager", "first"), CancellationToken.None);

        var act = () => _sut.Handle(new CreateRoleCommand("manager", "second"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Role with this name already exists.");
    }

    /// <summary>
    /// Mixed-case input normalizes to upper-case. The handler stores both
    /// <c>Name</c> (preserved case) and <c>NormalizedName</c> (uppercase). This
    /// contract is what makes the case-insensitive lookups work — if it
    /// regresses, "Manager" and "manager" would no longer collide.
    /// </summary>
    [Fact]
    public async Task Handle_WithMixedCaseName_StoresUpperCaseNormalizedName()
    {
        await _sut.Handle(new CreateRoleCommand("MaNaGeR", null), CancellationToken.None);

        var stored = _dbContext.Roles.Single();
        stored.Name.Should().Be("MaNaGeR");
        stored.NormalizedName.Should().Be("MANAGER");
    }
}