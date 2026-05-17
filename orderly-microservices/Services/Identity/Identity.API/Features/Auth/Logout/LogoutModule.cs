using OpenIddict.Abstractions;

namespace Identity.API.Features.Auth.Logout;

public class LogoutModule : CarterModule
{
    public LogoutModule() : base("/api/auth") { }

    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/logout", async (
                SignInManager<ApplicationUser> signInManager,
                IOpenIddictTokenManager tokenManager,
                ClaimsPrincipal principal,
                CancellationToken ct) =>
            {
                if (principal.Identity?.IsAuthenticated != true)
                {
                    return Results.NoContent();
                }

                var tokenId = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
                if (tokenId is not null)
                {
                    var token = await tokenManager.FindByIdAsync(tokenId, ct);
                    if (token is not null)
                    {
                        await tokenManager.TryRevokeAsync(token, ct);
                    }
                }

                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .Produces(204);
    }
}
