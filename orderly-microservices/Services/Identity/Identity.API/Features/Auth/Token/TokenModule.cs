namespace Identity.API.Features.Auth.Token;

public class TokenModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/connect");

        group.MapPost("/token", async (
                HttpContext context,
                ClaimsTransformer claimsTransformer,
                AuditLogger auditLogger,
                CancellationToken ct) =>
            {
                var request = context.GetOpenIddictServerRequest()
                    ?? throw new InvalidOperationException("Invalid token request.");

                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers.UserAgent.ToString();

                if (request.IsPasswordGrantType())
                {
                    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();

                    var user = await userManager.FindByEmailAsync(request.Username!);
                    if (user is null || !user.IsActive)
                    {
                        await auditLogger.LogAsync(user?.Id, "TokenFailure", ipAddress, userAgent, "User not found or inactive", ct);
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    var result = await signInManager.CheckPasswordSignInAsync(user, request.Password!, true);
                    if (!result.Succeeded)
                    {
                        await auditLogger.LogAsync(user.Id, "TokenFailure", ipAddress, userAgent, "Invalid credentials", ct);
                        return Results.Problem("Invalid credentials.", statusCode: 400);
                    }

                    await auditLogger.LogAsync(user.Id, "TokenIssued", ipAddress, userAgent, "Password grant", ct);

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

                    var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

                    await auditLogger.LogAsync(userId, "TokenRefreshed", ipAddress, userAgent, "Refresh token grant", ct);

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
        // Deliberately left on the global limiter rather than the strict
        // RateLimitPolicies.Auth budget. This one route serves two grant
        // types: `password` (brute-forceable, wants a tight budget) and
        // `refresh_token` (routine, high-frequency client traffic that a
        // 5-per-15-minute cap would break). Distinguishing them inside the
        // partitioner would mean reading the un-buffered form body. The
        // password path is instead covered by ASP.NET Identity's per-account
        // lockout — CheckPasswordSignInAsync is called with
        // lockoutOnFailure: true above — which is the per-account control the
        // per-IP limiter cannot be.
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

