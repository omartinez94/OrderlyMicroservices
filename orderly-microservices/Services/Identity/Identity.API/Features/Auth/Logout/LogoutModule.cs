using System.Security.Claims;
using Identity.API.Data;
using Identity.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Identity.API.Features.Auth.Logout;

public class LogoutModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        Delegate handler = async (
                HttpContext context,
                SignInManager<ApplicationUser> signInManager,
                IOpenIddictTokenManager tokenManager,
                ClaimsPrincipal principal,
                AuditLogger auditLogger,
                CancellationToken ct) =>
            {
                if (principal.Identity?.IsAuthenticated != true)
                {
                    AuthenticateResult authResult = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                    if (authResult.Succeeded && authResult.Principal is not null)
                    {
                        principal = authResult.Principal;
                    }
                }

                string? ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string? userAgent = context.Request.Headers.UserAgent.ToString();

                if (principal.Identity?.IsAuthenticated == true)
                {
                    string? userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

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
                }

                if (context.Request.Cookies.TryGetValue("refresh_token", out string? refreshToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    object? token = await tokenManager.FindByReferenceIdAsync(refreshToken, ct)
                        ?? await tokenManager.FindByIdAsync(refreshToken, ct);
                    if (token is not null)
                    {
                        await tokenManager.TryRevokeAsync(token, ct);
                    }
                }

                await signInManager.SignOutAsync();

                context.Response.Cookies.Delete("refresh_token", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });

                return Results.NoContent();
            };

        app.MapGroup("/v1/auth")
            .MapPost("/logout", handler)
            .Produces(204);

        app.MapGroup("/api/auth")
            .MapPost("/logout", handler)
            .Produces(204);
    }
}
