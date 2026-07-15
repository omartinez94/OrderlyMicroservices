using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Replaces the production JWT bearer handler in the Discount integration
/// test host. Reads <c>X-Test-User</c> + <c>X-Test-Permissions</c> from the
/// request and turns them into a <see cref="ClaimsPrincipal"/>. Requests
/// without those headers fall through and the gRPC authorization middleware
/// (<see cref="DiscountAuthorizationInterceptor"/>) returns
/// <c>StatusCode.Unauthenticated</c>. Mirrors Catalog's
/// <c>TestAuthHandler</c> exactly so the two test projects share an auth
/// convention.
/// </summary>
/// <remarks>
/// <para>gRPC integration tests pass <c>Metadata["x-test-user"]</c> +
/// <c>Metadata["x-test-permissions"]</c> on the outbound call. ASP.NET Core
/// gRPC server promotes arbitrary client metadata into the underlying
/// <c>HttpContext.Request.Headers</c> collection (the same key, lowercase
/// per HTTP/2 convention), so this handler picks them up unchanged.</para>
/// <para>Permissions are emitted one-claim-per-permission (Identity Shape A
/// canonical). The <see cref="Unit.DiscountPermissionsTests"/> Theories
/// assert that the canonical Shape A path returns true; the comma-split
/// Shape B path is asserted by the same handler when the test JWT payload
/// uses Shape B.</para>
/// </remarks>
public sealed class TestAuthHandler(
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

        var permissionsHeader = Request.Headers["X-Test-Permissions"].ToString();
        var permissions = string.IsNullOrWhiteSpace(permissionsHeader)
            ? []
            : permissionsHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"test-user-{userId}"),
            // Stable restaurant claim so the global query filter is satisfied
            // for tests that need tenant scoping.
            new("restaurantId", "11111111-1111-1111-1111-111111111111"),
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
