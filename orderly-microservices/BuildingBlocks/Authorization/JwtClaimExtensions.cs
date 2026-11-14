using System.Security.Claims;

namespace BuildingBlocks.Authorization;

public static class JwtClaimExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        string? userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdString, out var userId) ? userId : Guid.Empty;
    }

    public static Guid GetRestaurantId(this ClaimsPrincipal principal)
    {
        string? restaurantIdString = principal.FindFirstValue("restaurantId");
        return Guid.TryParse(restaurantIdString, out var restaurantId) ? restaurantId : Guid.Empty;
    }

    public static IEnumerable<Guid> GetRestaurantIds(this ClaimsPrincipal principal)
    {
        List<Guid> ids = [];
        foreach (Claim claim in principal.Claims)
        {
            if (claim.Type is "restaurantId" or "restaurantIds" or "restaurant_ids" &&
                Guid.TryParse(claim.Value, out Guid id) &&
                id != Guid.Empty &&
                !ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    public static bool HasRestaurantAccess(this ClaimsPrincipal principal, Guid restaurantId)
    {
        if (principal.IsInRole("SuperAdmin") ||
            principal.Claims.Any(c => (c.Type == ClaimTypes.Role || c.Type == "role") &&
                                      string.Equals(c.Value, "SuperAdmin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return principal.Claims.Any(c =>
            (c.Type is "restaurantId" or "restaurantIds" or "restaurant_ids") &&
            Guid.TryParse(c.Value, out Guid id) && id == restaurantId);
    }

    public static IEnumerable<string> GetPermissions(this ClaimsPrincipal principal)
    {
        return principal.Claims
            .Where(c => c.Type == "permissions")
            .Select(c => c.Value);
    }

    public static IEnumerable<string> GetRoles(this ClaimsPrincipal principal)
    {
        return principal.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value);
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        return GetPermissions(principal).Contains(permission);
    }
}
