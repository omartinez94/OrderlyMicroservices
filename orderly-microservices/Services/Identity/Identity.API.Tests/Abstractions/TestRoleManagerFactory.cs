namespace Identity.API.Tests.Abstractions;

/// <summary>
/// Constructs a real <see cref="RoleManager{TRole}"/> over the supplied in-memory
/// <see cref="IdentityDbContext"/> via <c>RoleStore&lt;ApplicationRole, IdentityDbContext,
/// Guid&gt;</c>. Mirrors <see cref="TestUserManagerFactory"/>: real store, no DI magic,
/// keeps normalized-name lookups on the production code path.
/// </summary>
internal static class TestRoleManagerFactory
{
    public static RoleManager<ApplicationRole> Create(IdentityDbContext dbContext)
    {
        var store = new RoleStore<ApplicationRole, IdentityDbContext, Guid>(dbContext);
        var normalizer = new UpperInvariantLookupNormalizer();
        var describer = new IdentityErrorDescriber();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RoleManager<ApplicationRole>>.Instance;

        return new RoleManager<ApplicationRole>(
            store,
            roleValidators: [],
            normalizer,
            describer,
            logger);
    }
}