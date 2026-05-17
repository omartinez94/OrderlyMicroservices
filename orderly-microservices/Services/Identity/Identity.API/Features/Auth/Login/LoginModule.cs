namespace Identity.API.Features.Auth.Login;

public class LoginModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
                HttpContext context,
                SignInManager<ApplicationUser> signInManager,
                UserManager<ApplicationUser> userManager,
                ClaimsTransformer claimsTransformer,
                IValidator<LoginRequest> validator,
                AuditLogger auditLogger,
                LoginRequest request,
                CancellationToken ct) =>
            {
                var validationResult = await validator.ValidateAsync(request, ct);
                if (!validationResult.IsValid)
                {
                    return Results.BadRequest(validationResult.Errors);
                }

                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers.UserAgent.ToString();

                var user = await userManager.FindByEmailAsync(request.Email);
                if (user is null || !user.IsActive)
                {
                    await auditLogger.LogAsync(user?.Id, "LoginFailure", ipAddress, userAgent, "User not found or inactive", ct);
                    return Results.Unauthorized();
                }

                var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, true);
                if (!result.Succeeded)
                {
                    if (result.IsLockedOut)
                    {
                        await auditLogger.LogAsync(user.Id, "AccountLocked", ipAddress, userAgent, "Account locked due to multiple failed attempts", ct);
                        return Results.Problem("Account is temporarily locked.", statusCode: 403);
                    }
                    
                    await auditLogger.LogAsync(user.Id, "LoginFailure", ipAddress, userAgent, "Invalid credentials", ct);
                    return Results.Unauthorized();
                }

                await auditLogger.LogAsync(user.Id, "LoginSuccess", ipAddress, userAgent, null, ct);

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

