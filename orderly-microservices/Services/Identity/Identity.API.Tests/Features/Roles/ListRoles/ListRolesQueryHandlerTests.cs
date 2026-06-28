using Identity.API.Features.Roles.ListRoles;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Roles.ListRoles;

/// <summary>
/// Covers the observable branches of <see cref="ListRolesQueryHandler"/>: the
/// empty case (fresh tenant), the multi-role case, and the null-description
/// pass-through. The handler is a pure projection over <c>roleManager.Roles</c>,
/// so the test surface is small but worth pinning.
/// </summary>
public sealed class ListRolesQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ListRolesQueryHandler _sut;

    public ListRolesQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new ListRolesQueryHandler(_roleManager);
    }

    /// <summary>
    /// Empty role store → empty list. Locks in the contract for a fresh
    /// tenant or a freshly-migrated database.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyStore_ReturnsEmptyList()
    {
        var response = await _sut.Handle(new ListRolesQuery(), CancellationToken.None);

        response.Roles.Should().BeEmpty();
    }

    /// <summary>
    /// Multiple roles are returned with the correct shape. Locks in the
    /// <c>(Id, Name, Description)</c> projection that the admin role table
    /// renders.
    /// </summary>
    [Fact]
    public async Task Handle_WithMultipleRoles_ReturnsAllInResponse()
    {
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager", "Operations"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Waiter", "Front of house"));

        var response = await _sut.Handle(new ListRolesQuery(), CancellationToken.None);

        response.Roles.Should().HaveCount(2);
        response.Roles.Select(r => r.Name).Should().BeEquivalentTo(new[] { "Manager", "Waiter" });
        response.Roles.Single(r => r.Name == "Manager").Description.Should().Be("Operations");
    }

    /// <summary>
    /// Role with null description is returned with <c>null</c>, not an empty
    /// string. Locks in the nullable-description contract.
    /// </summary>
    [Fact]
    public async Task Handle_WithNullDescription_ReturnsNull()
    {
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager", description: null));

        var response = await _sut.Handle(new ListRolesQuery(), CancellationToken.None);

        response.Roles.Single().Description.Should().BeNull();
    }
}