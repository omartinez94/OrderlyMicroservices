using Identity.API.Features.Permissions.ListPermissions;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Permissions.ListPermissions;

/// <summary>
/// Covers every observable branch of <see cref="ListPermissionsQueryHandler"/>:
/// the empty case, the all-fields projection, and the multi-row case. This
/// handler is a pure projection over <c>dbContext.Permissions</c>, so the test
/// surface is small.
/// </summary>
public sealed class ListPermissionsQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly ListPermissionsQueryHandler _sut;

    public ListPermissionsQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _sut = new ListPermissionsQueryHandler(_dbContext);
    }

    /// <summary>
    /// Empty permission table → empty list. Locks in the contract for a
    /// fresh tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyStore_ReturnsEmptyList()
    {
        var response = await _sut.Handle(new ListPermissionsQuery(), CancellationToken.None);

        response.Permissions.Should().BeEmpty();
    }

    /// <summary>
    /// All fields project correctly. Locks in the (Id, Name, Description,
    /// Resource, Action) shape that the admin permission table renders.
    /// </summary>
    [Fact]
    public async Task Handle_WithMultiplePermissions_ReturnsAllFields()
    {
        _dbContext.Permissions.Add(IdentityTestData.NewPermission(
            "users:view_all", description: "View all users", resource: "users", action: "view_all"));
        _dbContext.Permissions.Add(IdentityTestData.NewPermission(
            "orders:create", description: "Create orders", resource: "orders", action: "create"));
        await _dbContext.SaveChangesAsync();

        var response = await _sut.Handle(new ListPermissionsQuery(), CancellationToken.None);

        response.Permissions.Should().HaveCount(2);
        response.Permissions.Should().AllSatisfy(p =>
        {
            p.Id.Should().NotBe(Guid.Empty);
            p.Name.Should().NotBeNullOrEmpty();
            p.Resource.Should().NotBeNullOrEmpty();
            p.Action.Should().NotBeNullOrEmpty();
        });
        response.Permissions.Single(p => p.Name == "users:view_all").Description.Should().Be("View all users");
        response.Permissions.Single(p => p.Name == "orders:create").Resource.Should().Be("orders");
    }
}