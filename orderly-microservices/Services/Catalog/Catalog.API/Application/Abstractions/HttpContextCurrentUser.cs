namespace Catalog.API.Application.Abstractions;

/// <summary>
/// HTTP-context backed <see cref="ICurrentUser"/>. Reads the user id
/// from the JWT <c>sub</c> claim (matches <see cref="JwtClaimExtensions.UserId"/>).
/// Falls back to <c>null</c> when the claim is absent (anonymous endpoint)
/// or not parseable as a <see cref="Guid"/>.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return null;
            }

            Guid id = user.GetUserId();
            return id == Guid.Empty ? null : id;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public Guid? RestaurantId
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return null;
            }

            Guid restaurantId = user.GetRestaurantId();
            return restaurantId == Guid.Empty ? null : restaurantId;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> RestaurantIds
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return [];
            }

            List<Guid> ids = [];
            foreach (Claim claim in user.Claims)
            {
                if ((claim.Type == "restaurant_ids" || claim.Type == "restaurantId") &&
                    Guid.TryParse(claim.Value, out Guid id) &&
                    id != Guid.Empty &&
                    !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Roles
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return [];
            }

            return user.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
