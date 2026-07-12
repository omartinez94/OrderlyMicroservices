using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// Replaces the production JWT bearer handler in the integration test host.
/// Reads <c>X-Test-User</c> + <c>X-Test-Permissions</c> from the request and
/// turns them into a <see cref="ClaimsPrincipal"/>. Requests without those
/// headers fall through and the endpoint authorization middleware returns 401.
/// Mirrors <c>Ordering.API.Tests.Integration.TestAuthHandler</c>.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Scheme name registered in the test host.</summary>
    public const string SchemeName = "Test";

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userIdHeader = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(userIdHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(userIdHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Test-User is not a valid Guid."));
        }

        var permissions = Request.Headers["X-Test-Permissions"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"test-user-{userId}"),
        };
        foreach (var p in permissions)
        {
            claims.Add(new Claim("permissions", p));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
