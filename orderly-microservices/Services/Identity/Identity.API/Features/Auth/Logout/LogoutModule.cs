namespace Identity.API.Features.Auth.Logout;

public class LogoutModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/auth");

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

                string? userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

                string? ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string? userAgent = context.Request.Headers.UserAgent.ToString();

                string? tokenId = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
                if (tokenId is not null)
                {
                    object? token = await tokenManager.FindByIdAsync(tokenId, ct);
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

