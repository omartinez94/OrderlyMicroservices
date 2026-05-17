using OpenIddict.Abstractions;

namespace Identity.API.Features.Auth.Login;

public class LoginModule : CarterModule
{
    public LoginModule() : base("/api/auth") { }

    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/login", async (
                HttpContext context,
                SignInManager<ApplicationUser> signInManager,
                UserManager<ApplicationUser> userManager,
                ClaimsTransformer claimsTransformer,
                LoginRequest request,
                CancellationToken ct) =>
            {
                var user = await userManager.FindByEmailAsync(request.Email);
                if (user is null || !user.IsActive)
                {
                    return Results.Unauthorized();
                }

                var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, true);
                if (!result.Succeeded)
                {
                    return Results.Unauthorized();
                }

                var principal = await signInManager.CreateUserPrincipalAsync(user);
                principal.SetScopes([
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.OfflineAccess
                ]);

                var claims = await claimsTransformer.GenerateClaimsAsync(principal, ct);

                var identity = principal.Identity as ClaimsIdentity;
                if (identity != null)
                {
                    var existingClaims = identity.Claims.ToList();
                    foreach (var claim in existingClaims)
                    {
                        identity.RemoveClaim(claim);
                    }
                    foreach (var claim in claims)
                    {
                        identity.AddClaim(claim);
                    }
                }

                principal.SetDestinations(GetDestinations);

                return Results.SignIn(principal, properties: null, "OpenIddict.Server.AspNetCore");
            })
            .Accepts<LoginRequest>("application/json")
            .Produces<TokenResponse>()
            .ProducesProblem(401);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        return claim.Type switch
        {
            ClaimTypes.NameIdentifier or ClaimTypes.Email or ClaimTypes.Name
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            "firstName" or "lastName" or "isActive"
                => [OpenIddictConstants.Destinations.AccessToken],
            ClaimTypes.Role
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            "restaurantId"
                => [OpenIddictConstants.Destinations.AccessToken],
            "permissions"
                => [OpenIddictConstants.Destinations.AccessToken],
            _ => [OpenIddictConstants.Destinations.AccessToken]
        };
    }
}
