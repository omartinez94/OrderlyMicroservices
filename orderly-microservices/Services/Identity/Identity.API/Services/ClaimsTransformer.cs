using OpenIddict.Abstractions;

namespace Identity.API.Services;

public class ClaimsTransformer(IdentityDbContext dbContext)
{
    public async Task<ImmutableArray<Claim>> GenerateClaimsAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return [];

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return [];

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new("firstName", user.FirstName),
            new("lastName", user.LastName),
            new("isActive", user.IsActive.ToString()),
        };

        var roleNames = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .Where(r => r != null)
            .ToListAsync(ct);

        foreach (var role in roleNames)
        {
            if (role != null)
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var userRestaurants = await dbContext.UserRestaurants
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

        var defaultRestaurant = userRestaurants.FirstOrDefault(ur => ur.IsDefault) ?? userRestaurants.FirstOrDefault();
        if (defaultRestaurant != null)
        {
            claims.Add(new Claim("restaurantId", defaultRestaurant.RestaurantId.ToString()));
        }

        var permissions = await dbContext.RolePermissions
            .Where(rp => roleNames.Contains(rp.Role.Name))
            .Join(dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Name)
            .Distinct()
            .ToListAsync(ct);

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permissions", permission));
        }

        return claims.ToImmutableArray();
    }
}
