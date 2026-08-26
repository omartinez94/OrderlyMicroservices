namespace Identity.API.Features.Auth.Token;

public class TokenModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/connect");

        group.MapPost("/token", async (
                HttpContext context,
                ClaimsTransformer claimsTransformer,
                AuditLogger auditLogger,
                CancellationToken ct) =>
            {
                OpenIddictRequest request = context.GetOpenIddictServerRequest()
                    ?? throw new InvalidOperationException("Invalid token request.");

                string ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string userAgent = context.Request.Headers.UserAgent.ToString();

                if (request.IsPasswordGrantType())
                {
                    UserManager<ApplicationUser> userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    SignInManager<ApplicationUser> signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();

                    ApplicationUser? user = await userManager.FindByEmailAsync(request.Username!);
                    if (user is null || !user.IsActive)
                    {
                        await auditLogger.LogAsync(user?.Id, "TokenFailure", ipAddress, userAgent, "User not found or inactive", ct);
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    SignInResult result = await signInManager.CheckPasswordSignInAsync(user, request.Password!, true);
                    if (!result.Succeeded)
                    {
                        await auditLogger.LogAsync(user.Id, "TokenFailure", ipAddress, userAgent, "Invalid credentials", ct);
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    await auditLogger.LogAsync(user.Id, "TokenIssued", ipAddress, userAgent, "Password grant", ct);

                    ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);
                    principal.SetScopes(request.GetScopes());

                    ImmutableArray<Claim> claims = await claimsTransformer.GenerateClaimsAsync(principal, ct);
                    if (principal.Identity is ClaimsIdentity identity)
                    {
                        List<Claim> existingClaims = [.. identity.Claims];
                        foreach (Claim? claim in existingClaims)
                        {
                            identity.RemoveClaim(claim);
                        }
                        foreach (Claim claim in claims)
                        {
                            identity.AddClaim(claim);
                        }
                    }

                    principal.SetDestinations(GetDestinations);

                    return Results.SignIn(principal, properties: null, "OpenIddict.Server.AspNetCore");
                }

                if (request.IsRefreshTokenGrantType())
                {
                    AuthenticateResult authenticateResult = await context.AuthenticateAsync("OpenIddict.Server.AspNetCore");
                    ClaimsPrincipal principal = authenticateResult.Principal
                        ?? throw new InvalidOperationException("Invalid refresh token.");

                    string? userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    Guid? userId = Guid.TryParse(userIdString, out Guid parsedId) ? parsedId : null;

                    await auditLogger.LogAsync(userId, "TokenRefreshed", ipAddress, userAgent, "Refresh token grant", ct);

                    ImmutableArray<Claim> claims = await claimsTransformer.GenerateClaimsAsync(principal, ct);
                    var identity = principal.Identity as ClaimsIdentity;
                    if (identity is not null)
                    {
                        List<Claim> existingClaims = [.. identity.Claims];
                        foreach (Claim? claim in existingClaims)
                        {
                            identity.RemoveClaim(claim);
                        }
                        foreach (Claim claim in claims)
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
            _ => [OpenIddictConstants.Destinations.AccessToken],
        };
    }
}

