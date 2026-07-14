using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Multitenancy;

/// <summary>
/// Reads the tenant from the <c>restaurantId</c> claim on the current
/// <see cref="HttpContext.User"/>. Returns <see cref="Guid.Empty"/> when:
///   - there is no HTTP context (background services, design-time tooling), or
///   - the user is unauthenticated, or
///   - the claim is missing or not a valid <see cref="Guid"/>.
/// Returning <c>Guid.Empty</c> causes the global query filter to match no rows
/// — the fail-secure default. The tenant provider is intentionally the single
/// source of truth for tenant identity in a request scope; downstream code
/// reads from <see cref="ICurrentRestaurantProvider"/> only.
/// </summary>
public sealed class ClaimsRestaurantProvider(IHttpContextAccessor accessor) : ICurrentRestaurantProvider
{
    public Guid RestaurantId
    {
        get
        {
            var context = accessor.HttpContext;
            if (context?.User?.Identity?.IsAuthenticated != true)
            {
                return Guid.Empty;
            }

            var raw = context.User.FindFirstValue("restaurantId");
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
