using System.Security.Claims;

using System.Security.Claims;

namespace BuildingBlocks.Authorization;

public static class JwtClaimExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdString, out var userId) ? userId : Guid.Empty;
    }

    public static int GetRestaurantId(this ClaimsPrincipal principal)
    {
        var restaurantIdString = principal.FindFirstValue("restaurantId");
        return int.TryParse(restaurantIdString, out var restaurantId) ? restaurantId : 0;
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
