namespace Identity.API.Tests.Abstractions;

/// <summary>
/// Builds a fresh <see cref="IdentityDbContext"/> backed by the EF Core in-memory provider.
/// Each call returns a context with a unique database name so test classes running in
/// parallel cannot pollute one another. Password hashing is provider-agnostic, so the
/// real <see cref="UserManager{TUser}"/> continues to exercise <c>PasswordHasher&lt;&gt;</c>
/// exactly as production does — only the SQL dialect is mocked away.
/// </summary>
internal static class TestDbContextFactory
{
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new IdentityDbContext(options);
    }
}