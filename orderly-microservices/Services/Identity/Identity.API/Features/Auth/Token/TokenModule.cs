using System.Text.Json;

namespace Identity.API.Features.Auth.Token;

public class TokenModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        Delegate handler = async (
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

                    ImmutableArray<Claim> claims = await claimsTransformer.GenerateClaimsAsync(principal, ct);
                    if (principal.Identity is ClaimsIdentity identity)
                    {
                        ReplaceClaims(identity, claims);
                    }

                    principal.SetScopes(request.GetScopes());
                    principal.SetAudiences("OrderlyMicroservices");
                    principal.SetDestinations(GetDestinations);

                    StoreAuthResponseItems(context, principal);

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

                    List<string> scopes = [.. principal.GetScopes()];

                    ImmutableArray<Claim> claims = await claimsTransformer.GenerateClaimsAsync(principal, ct);
                    if (principal.Identity is ClaimsIdentity identity)
                    {
                        ReplaceClaims(identity, claims);
                    }

                    principal.SetScopes(scopes);
                    principal.SetAudiences("OrderlyMicroservices");
                    principal.SetDestinations(GetDestinations);

                    StoreAuthResponseItems(context, principal);

                    return Results.SignIn(principal, properties: null, "OpenIddict.Server.AspNetCore");
                }

                throw new InvalidOperationException("Unsupported grant type.");
            };

        app.MapGroup("/connect")
            .MapPost("/token", handler)
            .Produces<TokenResponse>()
            .ProducesProblem(400);

        app.MapGroup("/v1/auth")
            .MapPost("/refresh", handler)
            .Produces<TokenResponse>()
            .ProducesProblem(400);

        app.MapGroup("/api/auth")
            .MapPost("/refresh", handler)
            .Produces<TokenResponse>()
            .ProducesProblem(400);
    }

    /// <summary>
    /// Drops every claim currently on the identity and re-adds the
    /// supplied set in order. Preserves insertion order — OpenIddict
    /// serialises claims in document order, and the order produced by
    /// <see cref="ClaimsTransformer.GenerateClaimsAsync"/> is the
    /// contract that downstream JWT consumers rely on.
    /// </summary>
    private static void ReplaceClaims(ClaimsIdentity identity, ImmutableArray<Claim> claims)
    {
        List<Claim> existingClaims = identity.Claims
            .Where(c => !c.Type.StartsWith("oi_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (Claim claim in existingClaims)
        {
            identity.RemoveClaim(claim);
        }

        foreach (Claim claim in claims)
        {
            identity.AddClaim(claim);
        }
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

    private static void StoreAuthResponseItems(HttpContext context, ClaimsPrincipal principal)
    {
        string userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        string name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        string email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        string initials = name.Length >= 2 ? name[..2].ToUpperInvariant() : (name.ToUpperInvariant() ?? "U");
        string isActive = principal.FindFirstValue("isActive") ?? "True";

        string[] roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToArray();
        string[] permissions = principal.FindAll("permissions").Select(c => c.Value).Distinct().ToArray();

        context.Items["AuthResponse_User"] = JsonSerializer.SerializeToElement(new
        {
            id = userId,
            name = name,
            email = email,
            initials = initials,
            isActive = !string.Equals(isActive, "False", StringComparison.OrdinalIgnoreCase)
        });
        context.Items["AuthResponse_Roles"] = JsonSerializer.SerializeToElement(roles);
        context.Items["AuthResponse_Permissions"] = JsonSerializer.SerializeToElement(permissions);
    }
}

