using Identity.API.Features.Users.DeleteUser;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.DeleteUser;

/// <summary>
/// Covers every branch of <see cref="DeleteUserCommandHandler"/>: the happy
/// path that removes the user and all their restaurant assignments, the
/// not-found path, and the "no restaurants" / "many restaurants" boundary cases.
/// Delete is the only handler that spans two tables, so it is the only place
/// this kind of multi-step persistence regression can hide.
/// </summary>
public sealed class DeleteUserCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DeleteUserCommandHandler _sut;

    public DeleteUserCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _sut = new DeleteUserCommandHandler(_userManager, _dbContext);
    }

    private async Task<ApplicationUser> SeedUserWithRestaurantsAsync(params int[] restaurantIds)
    {
        var user = IdentityTestData.NewUser();
        var result = await _userManager.CreateAsync(user, "P@ssword1!");
        result.Succeeded.Should().BeTrue();

        foreach (var rid in restaurantIds)
        {
            _dbContext.UserRestaurants.Add(IdentityTestData.NewUserRestaurant(user, rid));
        }
        await _dbContext.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Happy path: the user row is removed, every <c>UserRestaurant</c> row
    /// for that user is removed. Without the second delete, the database would
    /// accumulate orphaned assignment rows that point at a non-existent user —
    /// exactly the kind of leak that breaks the login flow's restaurant
    /// selection down the line.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingUserAndRestaurants_RemovesBoth()
    {
        var user = await SeedUserWithRestaurantsAsync(1, 2, 3);

        await _sut.Handle(new DeleteUserCommand(user.Id), CancellationToken.None);

        _dbContext.Users.Should().BeEmpty();
        _dbContext.UserRestaurants.Should().BeEmpty();
    }

    /// <summary>
    /// User not found → <see cref="NotFoundException"/>. Without this guard,
    /// a typo in the user id would silently succeed and the caller would never
    /// know the operation had no effect.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new DeleteUserCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// User with no restaurant assignments still completes successfully. The
    /// <c>RemoveRange</c> over an empty query must not throw — otherwise every
    /// deletion of a freshly-registered user would fail.
    /// </summary>
    [Fact]
    public async Task Handle_WithUserWithoutRestaurants_StillSucceeds()
    {
        var user = await SeedUserWithRestaurantsAsync(); // no restaurants

        await _sut.Handle(new DeleteUserCommand(user.Id), CancellationToken.None);

        _dbContext.Users.Should().BeEmpty();
        _dbContext.UserRestaurants.Should().BeEmpty();
    }

    /// <summary>
    /// Deleting one user must not affect another user's restaurant assignments.
    /// Locks in the user-scoped filter on the cleanup query.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotAffectOtherUsersRestaurants()
    {
        var victim = await SeedUserWithRestaurantsAsync(10, 20);
        var bystander = IdentityTestData.NewUser("other@test.com");
        await _userManager.CreateAsync(bystander, "P@ssword1!");
        _dbContext.UserRestaurants.Add(IdentityTestData.NewUserRestaurant(bystander, 99));
        await _dbContext.SaveChangesAsync();

        await _sut.Handle(new DeleteUserCommand(victim.Id), CancellationToken.None);

        _dbContext.UserRestaurants.Should().HaveCount(1);
        _dbContext.UserRestaurants.Single().UserId.Should().Be(bystander.Id);
    }
}