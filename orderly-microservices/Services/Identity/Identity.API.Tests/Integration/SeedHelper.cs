using Identity.API.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.API.Tests.Integration;

/// <summary>
/// Shared seeding helpers for the Identity integration tests. Every
/// insert goes through a real <see cref="IdentityDbContext"/> resolved
/// from the factory's scope so the production
/// <c>AuditableEntityInterceptor</c> (created/updated/deleted audit
/// stamps) and the Identity user/role managers run on the same code
/// paths they do in production.
/// </summary>
internal static class SeedHelper
{
    /// <summary>
    /// Creates a user via the real <see cref="UserManager{TUser}"/> so
    /// password hashing matches what the production login flow checks
    /// against. Returns the new user's id; the user's email is exposed
    /// for tests that need to authenticate as them.
    /// </summary>
    /// <param name="factory">The shared WAF fixture.</param>
    /// <param name="email">Email address — also used as the username.</param>
    /// <param name="password">
    /// Must satisfy the production password policy
    /// (length ≥ 8, mixed case, digit, non-alphanumeric). Tests that
    /// want a weak-password response should pass it directly to the
    /// endpoint instead of going through this helper.
    /// </param>
    /// <param name="firstName">Defaults to "Test".</param>
    /// <param name="lastName">Defaults to "User".</param>
    public static async Task<(Guid UserId, string Email)> SeedUserAsync(
        this IdentityWebApplicationFactory factory,
        string email,
        string password = "P@ssword1!",
        string firstName = "Test",
        string lastName = "User")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"SeedUserAsync failed for '{email}': " +
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return (user.Id, email);
    }

    /// <summary>Inserts a minimal <see cref="LoginAuditLog"/> row directly via DbContext.</summary>
    public static async Task<Guid> SeedAuditLogAsync(
        this IdentityWebApplicationFactory factory,
        Guid? userId = null,
        string eventType = "LoginSuccess",
        string ipAddress = "127.0.0.1",
        string userAgent = "test-agent",
        string? details = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var log = new LoginAuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTimeOffset.UtcNow,
            Details = details,
        };

        db.LoginAuditLogs.Add(log);
        await db.SaveChangesAsync();
        return log.Id;
    }
}