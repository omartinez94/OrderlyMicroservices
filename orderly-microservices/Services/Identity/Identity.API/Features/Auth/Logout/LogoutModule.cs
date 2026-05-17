using OpenIddict.Abstractions;

namespace Identity.API.Features.Auth.Logout;

public class LogoutModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/logout", async (
                HttpContext context,
                SignInManager<ApplicationUser> signInManager,
                IOpenIddictTokenManager tokenManager,
                ClaimsPrincipal principal,
                AuditLogger auditLogger,
                CancellationToken ct) =>
            {
                if (principal.Identity?.IsAuthenticated != true)
                {
                    return Results.NoContent();
                }

                var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers.UserAgent.ToString();

                var tokenId = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
                if (tokenId is not null)
                {
                    var token = await tokenManager.FindByIdAsync(tokenId, ct);
                    if (token is not null)
                    {
                        await tokenManager.TryRevokeAsync(token, ct);
                    }
                }

                await auditLogger.LogAsync(userId, "Logout", ipAddress, userAgent, null, ct);

                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .Produces(204);
    }
}
