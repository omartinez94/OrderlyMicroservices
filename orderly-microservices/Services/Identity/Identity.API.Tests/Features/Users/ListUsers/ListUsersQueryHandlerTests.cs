using Identity.API.Features.Users.ListUsers;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.ListUsers;

/// <summary>
/// Covers every branch of <see cref="ListUsersQueryHandler"/>: the unfiltered
/// happy path, the search filter (across email, first name, last name), the
/// pagination math, and the <c>TotalCount</c> invariant.
/// </summary>
public sealed class ListUsersQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ListUsersQueryHandler _sut;

    public ListUsersQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new ListUsersQueryHandler(_userManager, _dbContext);
    }

    private async Task SeedUsersAsync(params (string email, string firstName, string lastName)[] users)
    {
        foreach (var (email, firstName, lastName) in users)
        {
            var u = IdentityTestData.NewUser(email, firstName, lastName);
            await _userManager.CreateAsync(u, "P@ssword1!");
        }
    }

    /// <summary>
    /// Happy path with the default paging. All users are returned, ordered by
    /// <c>LastName</c>, and <c>TotalCount</c> matches the unpaginated row count.
    /// </summary>
    [Fact]
    public async Task Handle_WithDefaultPaging_ReturnsAllUsersOrderedByLastName()
    {
        await SeedUsersAsync(
            ("alice@test.com", "Alice", "Adams"),
            ("bob@test.com", "Bob", "Brown"),
            ("carol@test.com", "Carol", "Carter"));

        var response = await _sut.Handle(new ListUsersQuery(), CancellationToken.None);

        response.TotalCount.Should().Be(3);
        response.Users.Select(u => u.LastName).Should().ContainInOrder("Adams", "Brown", "Carter");
    }

    /// <summary>
    /// SearchTerm filters across all three text columns. Locks in the
    /// "search anywhere in email/firstName/lastName" contract that the admin
    /// search box advertises.
    /// </summary>
    [Fact]
    public async Task Handle_WithSearchTerm_FiltersAcrossAllNameColumns()
    {
        await SeedUsersAsync(
            ("alice@example.com", "Alice", "Adams"),
            ("bob@example.com", "Bob", "Brown"),
            ("carol@example.com", "Carol", "Carter"));

        var byFirstName = await _sut.Handle(new ListUsersQuery(SearchTerm: "alice"), CancellationToken.None);
        byFirstName.TotalCount.Should().Be(1);
        byFirstName.Users.Single().Email.Should().Be("alice@example.com");

        var byLastName = await _sut.Handle(new ListUsersQuery(SearchTerm: "Brown"), CancellationToken.None);
        byLastName.TotalCount.Should().Be(1);
        byLastName.Users.Single().Email.Should().Be("bob@example.com");

        var byEmailDomain = await _sut.Handle(new ListUsersQuery(SearchTerm: "example"), CancellationToken.None);
        byEmailDomain.TotalCount.Should().Be(3);
    }

    /// <summary>
    /// Whitespace-only search term is treated as no filter. This matches what
    /// an empty form submission looks like after model binding strips trailing
    /// whitespace.
    /// </summary>
    [Fact]
    public async Task Handle_WithWhitespaceSearchTerm_ReturnsAllUsers()
    {
        await SeedUsersAsync(
            ("a@test.com", "A", "A"),
            ("b@test.com", "B", "B"));

        var response = await _sut.Handle(new ListUsersQuery(SearchTerm: "   "), CancellationToken.None);

        response.TotalCount.Should().Be(2);
    }

    /// <summary>
    /// Pagination: page 2 with pageSize 1 returns the second user (by
    /// <c>LastName</c> ordering) and the <c>TotalCount</c> reflects the
    /// unpaginated count of 3.
    /// </summary>
    [Fact]
    public async Task Handle_WithPagination_ReturnsCorrectSliceAndTotalCount()
    {
        await SeedUsersAsync(
            ("a@test.com", "Alice", "Adams"),
            ("b@test.com", "Bob", "Brown"),
            ("c@test.com", "Carol", "Carter"));

        var response = await _sut.Handle(new ListUsersQuery(Page: 2, PageSize: 1), CancellationToken.None);

        response.Users.Should().HaveCount(1);
        response.Users.Single().LastName.Should().Be("Brown");
        response.TotalCount.Should().Be(3); // not the paginated count
    }

    /// <summary>
    /// Empty store → empty list and zero count. Locks in the contract for a
    /// fresh tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyStore_ReturnsEmptyResult()
    {
        var response = await _sut.Handle(new ListUsersQuery(), CancellationToken.None);

        response.Users.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    /// <summary>
    /// Roles are loaded per-user into the response. Pins the projection shape
    /// that the admin table renders.
    /// </summary>
    [Fact]
    public async Task Handle_PopulatesRolesPerUser()
    {
        await SeedUsersAsync(("a@test.com", "Alice", "Adams"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager"));
        var user = _dbContext.Users.Single();
        await _userManager.AddToRoleAsync(user, "Manager");

        var response = await _sut.Handle(new ListUsersQuery(), CancellationToken.None);

        response.Users.Single().Roles.Should().BeEquivalentTo(new[] { "Manager" });
    }
}