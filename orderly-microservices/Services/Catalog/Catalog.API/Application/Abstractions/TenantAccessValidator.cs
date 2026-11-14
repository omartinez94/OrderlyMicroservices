namespace Catalog.API.Application.Abstractions;

/// <summary>
/// Tenant isolation helper extensions over <see cref="ICurrentUser"/>.
/// </summary>
public static class TenantAccessValidator
{
    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>
    /// Ensures the current user is authorized to access resources belonging to <paramref name="restaurantId"/>.
    /// Passes through when <paramref name="currentUser"/> is <c>null</c> or represents an unconfigured unit-test stub
    /// (<c>!IsAuthenticated</c>, <c>UserId is null</c>, <c>RestaurantId is null</c>, and <c>Roles.Count == 0</c>).
    /// </summary>
    /// <param name="currentUser">The current user context.</param>
    /// <param name="restaurantId">The target restaurant tenant identifier.</param>
    /// <exception cref="ForbiddenException">
    /// Thrown when the caller has identity claims, is not a <c>SuperAdmin</c>, and does not match <paramref name="restaurantId"/>.
    /// </exception>
    public static void EnsureTenantAccess(this ICurrentUser? currentUser, Guid restaurantId)
    {
        if (currentUser is null)
        {
            return;
        }

        IReadOnlyList<string> roles = currentUser.Roles ?? [];
        bool hasClaims = currentUser.IsAuthenticated
            || currentUser.UserId is not null
            || currentUser.RestaurantId is not null
            || roles.Count > 0;

        if (!hasClaims)
        {
            return;
        }

        bool isSuperAdmin = roles.Any(role =>
            string.Equals(role, SuperAdminRole, StringComparison.OrdinalIgnoreCase));

        if (isSuperAdmin)
        {
            return;
        }

        bool hasAccess = (currentUser.RestaurantId.HasValue && currentUser.RestaurantId.Value == restaurantId)
            || (currentUser.RestaurantIds is not null && currentUser.RestaurantIds.Contains(restaurantId));

        if (!hasAccess)
        {
            throw new ForbiddenException("Caller is not authorized to access resources for this restaurant.");
        }
    }
}

/// <summary>
/// Validates that the authenticated caller belongs to the target restaurant tenant
/// or holds the <c>SuperAdmin</c> role.
/// </summary>
public interface ITenantAccessValidator
{
    /// <summary>
    /// Ensures the current caller is authorized to access resources belonging to <paramref name="restaurantId"/>.
    /// </summary>
    /// <param name="restaurantId">The target restaurant tenant identifier.</param>
    void EnsureTenantAccess(Guid restaurantId);
}

/// <summary>
/// Default <see cref="ITenantAccessValidator"/> backed by <see cref="ICurrentUser"/>.
/// </summary>
public sealed class DefaultTenantAccessValidator(ICurrentUser currentUser) : ITenantAccessValidator
{
    /// <inheritdoc />
    public void EnsureTenantAccess(Guid restaurantId)
    {
        currentUser.EnsureTenantAccess(restaurantId);
    }
}
