namespace Identity.API.Tests.Abstractions;

/// <summary>
/// Builders for the Identity domain models. Centralizes <see langword="required"/>
/// property initialization so individual tests stay focused on the scenario under
/// test. Every method returns a freshly-constructed instance — callers are free to
/// mutate after the fact, but the defaults are the values production code would set
/// for a fresh seed row.
/// </summary>
internal static class IdentityTestData
{
    private const string DefaultEmailDomain = "test.com";

    /// <summary>Builds an <see cref="ApplicationUser"/> that satisfies every <c>required</c> constraint.</summary>
    public static ApplicationUser NewUser(
        string email = "user@test.com",
        string firstName = "Jane",
        string lastName = "Doe",
        bool isActive = true)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            EmailConfirmed = false,
        };
    }

    /// <summary>Builds an <see cref="ApplicationRole"/>.</summary>
    public static ApplicationRole NewRole(string name = "Manager", string? description = null)
    {
        return new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
        };
    }

    /// <summary>Builds a <see cref="Permission"/>. Name follows the production <c>"resource:action"</c> convention.</summary>
    public static Permission NewPermission(
        string name = "users:view_all",
        string? description = null,
        string? resource = null,
        string? action = null)
    {
        var parts = name.Split(':', 2);
        return new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Resource = resource ?? parts[0],
            Action = action ?? (parts.Length > 1 ? parts[1] : string.Empty),
        };
    }

    /// <summary>Builds a <see cref="RolePermission"/> junction row linking an in-memory role and permission.</summary>
    public static RolePermission NewRolePermission(ApplicationRole role, Permission permission)
    {
        return new RolePermission
        {
            RoleId = role.Id,
            Role = role,
            PermissionId = permission.Id,
            Permission = permission,
        };
    }

    /// <summary>Builds a <see cref="UserRestaurant"/> assignment.</summary>
    public static UserRestaurant NewUserRestaurant(
        ApplicationUser user,
        int restaurantId = 1,
        bool isDefault = false)
    {
        return new UserRestaurant
        {
            UserId = user.Id,
            User = user,
            RestaurantId = restaurantId,
            IsDefault = isDefault,
        };
    }

    /// <summary>Builds a <see cref="LoginAuditLog"/> row. Used by assertions that need to seed logs directly.</summary>
    public static LoginAuditLog NewAuditLog(
        Guid? userId = null,
        string eventType = "LoginSuccess",
        string ipAddress = "127.0.0.1",
        string userAgent = "test-agent",
        string? details = null,
        DateTimeOffset? timestamp = null)
    {
        return new LoginAuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Details = details,
        };
    }

    /// <summary>
    /// Seeds a permission, then a role-permission link, returning the permission name
    /// for use in handler assertions. Useful for <c>ClaimsTransformer</c> tests.
    /// </summary>
    public static async Task<Permission> SeedPermissionAsync(
        IdentityDbContext dbContext,
        string name = "users:view_all",
        string? description = null)
    {
        var permission = NewPermission(name, description);
        dbContext.Permissions.Add(permission);
        await dbContext.SaveChangesAsync();
        return permission;
    }
}