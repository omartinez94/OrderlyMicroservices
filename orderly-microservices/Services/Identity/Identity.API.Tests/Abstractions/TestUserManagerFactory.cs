namespace Identity.API.Tests.Abstractions;

/// <summary>
/// Constructs a real <see cref="UserManager{TUser}"/> over the supplied in-memory
/// <see cref="IdentityDbContext"/> via <c>UserStore&lt;ApplicationUser, ApplicationRole,
/// IdentityDbContext, Guid&gt;</c>. Using a real manager — rather than a mock — keeps
/// password hashing, normalized lookups, and role assignment on the production code
/// paths, which is the entire point of testing the handlers in the first place.
///
/// <para>
/// <b>Password policy is mirrored from <c>IdentityDbContextExtensions</c> in
/// production</b>: <c>RequiredLength=8</c>, <c>RequireDigit</c>,
/// <c>RequireNonAlphanumeric</c>, <c>RequireUppercase</c>, <c>RequireLowercase</c>,
/// <c>RequireUniqueEmail=true</c>. Without this, a password like <c>"weak"</c> would
/// pass <c>UserManager.CreateAsync</c> in tests but fail in production, making the
/// handlers' <c>BadRequestException</c> branches untestable.
/// </para>
/// </summary>
internal static class TestUserManagerFactory
{
    public static UserManager<ApplicationUser> Create(IdentityDbContext dbContext)
    {
        var store = new UserStore<ApplicationUser, ApplicationRole, IdentityDbContext, Guid>(dbContext);

        var options = Microsoft.Extensions.Options.Options.Create(new IdentityOptions
        {
            Password = new PasswordOptions
            {
                RequiredLength = 8,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                RequireUppercase = true,
                RequireLowercase = true,
            },
            User = new UserOptions
            {
                RequireUniqueEmail = true,
            },
        });

        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var normalizer = new UpperInvariantLookupNormalizer();
        var describer = new IdentityErrorDescriber();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManager<ApplicationUser>>.Instance;

        return new UserManager<ApplicationUser>(
            store,
            options,
            passwordHasher,
            userValidators: [new UserValidator<ApplicationUser>()],
            passwordValidators: [new PasswordValidator<ApplicationUser>()],
            normalizer,
            describer,
            services: null!,
            logger);
    }
}