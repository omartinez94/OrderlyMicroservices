using Identity.API.Features.Users.UpdateUser;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Users.UpdateUser;

/// <summary>
/// Covers every branch of <see cref="UpdateUserCommandHandler"/>: the happy
/// path that mutates the four mutable fields, the not-found path, and the
/// nullable / falsy edge cases on <c>PhoneNumber</c> and <c>IsActive</c>.
/// </summary>
public sealed class UpdateUserCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UpdateUserCommandHandler _sut;

    public UpdateUserCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _sut = new UpdateUserCommandHandler(_userManager, _dbContext);
    }

    private async Task<ApplicationUser> SeedUserAsync(string email = "user@test.com")
    {
        var user = IdentityTestData.NewUser(email);
        var result = await _userManager.CreateAsync(user, "P@ssword1!");
        result.Succeeded.Should().BeTrue();
        return user;
    }

    /// <summary>
    /// Happy path: every mutable field is updated, the response reflects the new
    /// state, and the underlying row matches. Locks in the contract that the
    /// admin endpoint can rename, re-phone, and (de)activate a user in one call.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingUser_UpdatesAllMutableFields()
    {
        var user = await SeedUserAsync();

        var response = await _sut.Handle(
            new UpdateUserCommand(user.Id, "Ada", "Lovelace", "+15550000000", true),
            CancellationToken.None);

        var stored = _dbContext.Users.Single();
        stored.FirstName.Should().Be("Ada");
        stored.LastName.Should().Be("Lovelace");
        stored.PhoneNumber.Should().Be("+15550000000");
        stored.IsActive.Should().BeTrue();
        response.Email.Should().Be(user.Email); // Email is immutable.
        response.UserId.Should().Be(user.Id);
    }

    /// <summary>
    /// User not found → <see cref="NotFoundException"/>. Without this guard, the
    /// handler would silently succeed and the caller would receive a default
    /// response, masking a bug where the wrong id was passed.
    /// </summary>
    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsNotFound()
    {
        var act = () => _sut.Handle(new UpdateUserCommand(Guid.NewGuid(), "X", "Y", null, true), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Null phone number and <c>IsActive = false</c> must persist as written.
    /// The handler must not coerce either value — a user who is deactivated
    /// should stay deactivated, and a user without a phone should stay
    /// without a phone — across subsequent updates.
    /// </summary>
    [Fact]
    public async Task Handle_WithNullPhoneAndInactive_PersistsAsWritten()
    {
        var user = await SeedUserAsync();

        await _sut.Handle(
            new UpdateUserCommand(user.Id, "Ada", "Lovelace", PhoneNumber: null, IsActive: false),
            CancellationToken.None);

        var stored = _dbContext.Users.Single();
        stored.PhoneNumber.Should().BeNull();
        stored.IsActive.Should().BeFalse();
    }
}