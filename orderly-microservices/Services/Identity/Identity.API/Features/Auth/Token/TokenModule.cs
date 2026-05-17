using OpenIddict.Abstractions;

namespace Identity.API.Features.Auth.Token;

public class TokenModule : CarterModule
{
    public TokenModule() : base("/connect") { }

    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/token", async (
                HttpContext context,
                ClaimsTransformer claimsTransformer,
                CancellationToken ct) =>
            {
                var request = context.GetOpenIddictServerRequest()
                    ?? throw new InvalidOperationException("Invalid token request.");

                if (request.IsPasswordGrantType())
                {
                    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();

                    var user = await userManager.FindByEmailAsync(request.Username!);
                    if (user is null || !user.IsActive)
                    {
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    var result = await signInManager.CheckPasswordSignInAsync(user, request.Password!, true);
                    if (!result.Succeeded)
                    {
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    var principal = await signInManager.CreateUserPrincipalAsync(user);
                    principal.SetScopes(request.GetScopes());

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
                }

                if (request.IsRefreshTokenGrantType())
                {
                    var authenticateResult = await context.AuthenticateAsync("OpenIddict.Server.AspNetCore");
                    var principal = authenticateResult.Principal
                        ?? throw new InvalidOperationException("Invalid refresh token.");

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
                }

                throw new InvalidOperationException("Unsupported grant type.");
            })
            .Produces<TokenResponse>()
            .ProducesProblem(400);
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
