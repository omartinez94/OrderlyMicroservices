using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Replaces the production JWT bearer handler in the integration test
/// host. Reads <c>X-Test-User</c>, <c>X-Test-Permissions</c>, and optional
/// <c>X-Test-Restaurant</c> from the request and turns them into a
/// <see cref="ClaimsPrincipal"/>. Requests without <c>X-Test-User</c> fall
/// through to the regular pipeline and get 401 — letting integration tests
/// assert the unauthenticated path without juggling JWT tokens.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    public static readonly Guid DefaultRestaurantId = new("11111111-1111-1111-1111-111111111111");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string userIdHeader = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(userIdHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(userIdHeader, out Guid userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Test-User is not a valid Guid."));
        }

        string[] permissions = Request.Headers["X-Test-Permissions"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"test-user-{userId}"),
        ];

        string restaurantHeader = Request.Headers["X-Test-Restaurant"].ToString();
        if (string.IsNullOrWhiteSpace(restaurantHeader))
        {
            claims.Add(new Claim("restaurantId", DefaultRestaurantId.ToString()));
        }
        else if (!string.Equals(restaurantHeader, "none", StringComparison.OrdinalIgnoreCase)
                 && Guid.TryParse(restaurantHeader, out Guid restaurantId))
        {
            claims.Add(new Claim("restaurantId", restaurantId.ToString()));
        }

        foreach (string p in permissions)
        {
            claims.Add(new Claim("permissions", p));
        }

        ClaimsIdentity identity = new(claims, SchemeName);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
