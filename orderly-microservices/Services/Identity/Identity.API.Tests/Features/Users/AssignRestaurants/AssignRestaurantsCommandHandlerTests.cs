using Identity.API.Features.Users.AssignRestaurants;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.AssignRestaurants;

/// <summary>
/// Covers every branch of <see cref="AssignRestaurantsCommandHandler"/>: the
/// replace-existing-assignments happy path, the not-found path, the empty
/// list case, and the (lack of) uniqueness enforcement on <c>IsDefault</c>.
/// AssignRestaurants is the operator's tool for moving a user between
/// restaurants, so it is exercised heavily in production.
/// </summary>
public sealed class AssignRestaurantsCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AssignRestaurantsCommandHandler _sut;

    public AssignRestaurantsCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _sut = new AssignRestaurantsCommandHandler(_userManager, _dbContext);
    }

    private async Task<ApplicationUser> SeedUserWithRestaurantsAsync(params (Guid id, bool isDefault)[] assignments)
    {
        var user = IdentityTestData.NewUser();
        await _userManager.CreateAsync(user, "P@ssword1!");
        foreach (var (rid, isDefault) in assignments)
        {
            _dbContext.UserRestaurants.Add(IdentityTestData.NewUserRestaurant(user, rid, isDefault));
        }
        await _dbContext.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Happy path: the existing assignments are wiped and the new ones are
    /// written. Without the wipe, repeated calls would accumulate rows and
    /// eventually cause a duplicate-row error on the (UserId, RestaurantId)
    /// unique index.
    /// </summary>
    [Fact]
    public async Task Handle_ReplacesExistingAssignments()
    {
        var seedRid1 = Guid.NewGuid();
        var seedRid2 = Guid.NewGuid();
        var newRid1 = Guid.NewGuid();
        var newRid2 = Guid.NewGuid();
        var newRid3 = Guid.NewGuid();
        var user = await SeedUserWithRestaurantsAsync((seedRid1, true), (seedRid2, false));

        var newAssignments = new List<RestaurantAssignment>
        {
            new(newRid1, true),
            new(newRid2, false),
            new(newRid3, false),
        };

        await _sut.Handle(new AssignRestaurantsCommand(user.Id, newAssignments), CancellationToken.None);

        var stored = _dbContext.UserRestaurants.Where(ur => ur.UserId == user.Id).ToList();
        stored.Select(ur => ur.RestaurantId).Should().BeEquivalentTo(new[] { newRid1, newRid2, newRid3 });
        stored.Single(ur => ur.RestaurantId == newRid1).IsDefault.Should().BeTrue();
    }

    /// <summary>
    /// User not found → <see cref="NotFoundException"/>. Without this guard,
    /// a typo in the user id would silently succeed.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new AssignRestaurantsCommand(Guid.NewGuid(), []), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Empty list clears every assignment. This is the de-facto "remove user
    /// from all restaurants" affordance; admin tooling depends on it.
    /// </summary>
    [Fact]
    public async Task Handle_WithEmptyList_RemovesAllAssignments()
    {
        var seedRid1 = Guid.NewGuid();
        var seedRid2 = Guid.NewGuid();
        var user = await SeedUserWithRestaurantsAsync((seedRid1, true), (seedRid2, false));

        await _sut.Handle(new AssignRestaurantsCommand(user.Id, []), CancellationToken.None);

        _dbContext.UserRestaurants.Where(ur => ur.UserId == user.Id).Should().BeEmpty();
    }

    /// <summary>
    /// Multiple rows can carry <c>IsDefault = true</c>. The handler does not
    /// enforce a single default — it persists the input verbatim. The downstream
    /// <c>ClaimsTransformer</c> picks one (the first <c>IsDefault = true</c>
    /// row), but the database is allowed to contain the inconsistency. This
    /// test pins the current behavior so any future tightening is intentional.
    /// </summary>
    [Fact]
    public async Task Handle_WithMultipleDefaults_PersistsAllDefaults()
    {
        var user = await SeedUserWithRestaurantsAsync();

        var rid1 = Guid.NewGuid();
        var rid2 = Guid.NewGuid();
        var rid3 = Guid.NewGuid();
        await _sut.Handle(new AssignRestaurantsCommand(user.Id, new List<RestaurantAssignment>
        {
            new(rid1, true),
            new(rid2, true),
            new(rid3, false),
        }), CancellationToken.None);

        var stored = _dbContext.UserRestaurants.Where(ur => ur.UserId == user.Id).ToList();
        stored.Count(ur => ur.IsDefault).Should().Be(2);
    }
}