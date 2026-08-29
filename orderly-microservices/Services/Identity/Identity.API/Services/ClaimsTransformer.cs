namespace Identity.API.Services;

public class ClaimsTransformer(IdentityDbContext dbContext)
{
    public async Task<ImmutableArray<Claim>> GenerateClaimsAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        string? userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return [];
        }

        ApplicationUser? user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return [];
        }

        List<Claim>? claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            // OpenIddict requires the `sub` claim by that exact type for
            // its sign-in handler; ASP.NET Core's ClaimTypes.NameIdentifier
            // alias isn't auto-mapped. Without this, Results.SignIn
            // throws InvalidOperationException ("the mandatory subject
            // claim was missing") at /connect/token (TokenModule.cs:62)
            // and would have done the same at the pre-fix /v1/auth/login.
            // The canonical pattern (kevinchalet.com/2023/10/04) copies
            // NameIdentifier → sub via an OpenIddict ProcessSignInContext
            // handler; doing it directly here keeps the transformer as
            // the single source of truth for the principal's claims.
            new(OpenIddictConstants.Claims.Subject, userId.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new("firstName", user.FirstName),
            new("lastName", user.LastName),
            new("isActive", user.IsActive.ToString()),
        };

        List<string?> roleNames = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .Where(r => r != null)
            .ToListAsync(ct);

        foreach (string? role in roleNames)
        {
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        List<UserRestaurant> userRestaurants = await dbContext.UserRestaurants
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

        UserRestaurant? defaultRestaurant = userRestaurants.FirstOrDefault(ur => ur.IsDefault) ?? userRestaurants.FirstOrDefault();
        if (defaultRestaurant is not null)
        {
            claims.Add(new Claim("restaurantId", defaultRestaurant.RestaurantId.ToString()));
        }

        List<string> permissions = await dbContext.RolePermissions
            .Where(rp => roleNames.Contains(rp.Role.Name))
            .Join(dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Name)
            .Distinct()
            .ToListAsync(ct);

        foreach (string permission in permissions)
        {
            claims.Add(new Claim("permissions", permission));
        }

        return claims.ToImmutableArray();
    }
}
