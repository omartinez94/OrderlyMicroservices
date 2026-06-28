using Identity.API.Features.Auth.Register;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.Auth.Register;

/// <summary>
/// Covers every branch of <see cref="RegisterCommandHandler"/>: the happy path,
/// the duplicate-email guard, the Identity-errors aggregation path, and the
/// <c>null</c>-phone-number pass-through. Register is the public entry point
/// for new users, so any regression here has immediate security implications
/// (e.g. silently allowing duplicate accounts, or losing the audit-log row).
/// </summary>
public sealed class RegisterCommandHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuditLogger _auditLogger;
    private readonly RegisterCommandHandler _sut;

    public RegisterCommandHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _auditLogger = new AuditLogger(_dbContext);
        _sut = new RegisterCommandHandler(_userManager, _auditLogger);
    }

    private static RegisterCommand NewCommand(
        string email = "new@test.com",
        string password = "P@ssword1!",
        string firstName = "Jane",
        string lastName = "Doe",
        string? phoneNumber = null)
        => new(new RegisterRequest(email, password, firstName, lastName, phoneNumber));

    // -------- Happy path --------

    /// <summary>
    /// Happy path: a brand-new email creates a user, returns the expected
    /// <see cref="RegisterResponse"/>, and writes exactly one
    /// <c>RegisterSuccess</c> audit-log row. This is the contract the public
    /// register endpoint depends on — if any of the three side-effects
    /// regress, downstream login fails or the audit trail loses the event.
    /// </summary>
    [Fact]
    public async Task Handle_WithNewEmail_CreatesUserAndAuditsRegisterSuccess()
    {
        var command = NewCommand(email: "new@test.com", firstName: "Ada", lastName: "Lovelace");

        var response = await _sut.Handle(command, CancellationToken.None);

        var stored = _dbContext.Users.Single();
        stored.Email.Should().Be("new@test.com");
        stored.FirstName.Should().Be("Ada");
        stored.LastName.Should().Be("Lovelace");
        stored.IsActive.Should().BeTrue();
        stored.Id.Should().NotBe(Guid.Empty);

        response.UserId.Should().Be(stored.Id);
        response.Email.Should().Be("new@test.com");
        response.FirstName.Should().Be("Ada");
        response.LastName.Should().Be("Lovelace");

        var log = _dbContext.LoginAuditLogs.Single();
        log.UserId.Should().Be(stored.Id);
        log.EventType.Should().Be("RegisterSuccess");
        log.IpAddress.Should().Be("N/A"); // Register handler hard-codes IP/UA — no HttpContext dependency.
        log.UserAgent.Should().Be("N/A");
        log.Details.Should().Be("User registered successfully");
    }

    /// <summary>
    /// Null phone number is allowed through the handler. Validator enforces
    /// the length rule only when the value is present; the handler must
    /// forward <c>null</c> to <c>UserManager.CreateAsync</c> unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_WithNullPhoneNumber_PersistsAsNull()
    {
        var command = NewCommand(phoneNumber: null);

        await _sut.Handle(command, CancellationToken.None);

        _dbContext.Users.Single().PhoneNumber.Should().BeNull();
    }

    /// <summary>
    /// Non-null phone number propagates verbatim. Locks in the field
    /// passthrough so the handler doesn't accidentally null-coalesce on
    /// the wrong side of an optional.
    /// </summary>
    [Fact]
    public async Task Handle_WithPhoneNumber_PersistsValue()
    {
        var command = NewCommand(phoneNumber: "+15551234567");

        await _sut.Handle(command, CancellationToken.None);

        _dbContext.Users.Single().PhoneNumber.Should().Be("+15551234567");
    }

    // -------- Duplicate email --------

    /// <summary>
    /// Duplicate email → <see cref="BadRequestException"/> with the expected
    /// message, and no audit row is written. The audit log only records
    /// successful registrations; failed duplicates are surfaced as 400 to the
    /// caller, not as anonymous audit events.
    /// </summary>
    [Fact]
    public async Task Handle_WithExistingEmail_ThrowsBadRequest()
    {
        // Seed a user with the target email via the real UserManager so
        // normalization matches what the handler will compare against.
        await _userManager.CreateAsync(IdentityTestData.NewUser("dup@test.com"), "P@ssword1!");

        var act = () => _sut.Handle(NewCommand(email: "dup@test.com"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("User with this email already exists.");
        _dbContext.LoginAuditLogs.Should().BeEmpty();
    }

    // -------- Identity errors --------

    /// <summary>
    /// Weak password → <c>UserManager.CreateAsync</c> returns failure → handler
    /// aggregates every error description into a single
    /// <see cref="BadRequestException"/> message. The aggregation matters: the
    /// caller needs every reason their password was rejected, not just the
    /// first one, so they can fix all violations in a single retry.
    /// </summary>
    [Fact]
    public async Task Handle_WithIdentityErrors_ThrowsBadRequestWithJoinedDescriptions()
    {
        // "weak" violates RequiredLength(8), RequireDigit, RequireNonAlphanumeric,
        // RequireUppercase. All four errors should be joined into the message.
        var act = () => _sut.Handle(NewCommand(password: "weak"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.Message.Should().StartWith("Registration failed:");
        ex.Which.Message.Should().Contain("uppercase");
        ex.Which.Message.Should().Contain("digit");
        ex.Which.Message.Should().Contain("non alphanumeric");
        _dbContext.LoginAuditLogs.Should().BeEmpty();
    }

    /// <summary>
    /// Identity errors path does not partially commit: no user row is created
    /// even though <c>UserManager</c> adds the user to the change tracker
    /// before validation. Locks in the transactional boundary so a failed
    /// registration cannot leak a half-built user.
    /// </summary>
    [Fact]
    public async Task Handle_WithIdentityErrors_DoesNotPersistUser()
    {
        var act = () => _sut.Handle(NewCommand(password: "weak"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        _dbContext.Users.Should().BeEmpty();
    }
}