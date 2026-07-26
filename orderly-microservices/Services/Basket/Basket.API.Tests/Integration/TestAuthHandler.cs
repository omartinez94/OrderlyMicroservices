using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;

namespace Basket.API.Tests.Integration;

/// <summary>
/// Replaces the production JWT bearer handler in the Basket integration
/// test host. Reads <c>X-Test-User</c> + <c>X-Test-Permissions</c> from
/// the request and turns them into a <see cref="ClaimsPrincipal"/>.
/// Requests without those headers fall through and the endpoint
/// authorization middleware returns 401.
/// </summary>
/// <remarks>
/// <para>Mirrors the established solution convention from
/// <c>Catalog.API.Tests.Integration.TestAuthHandler</c>,
/// <c>Ordering.API.Tests.Integration.TestAuthHandler</c>,
/// <c>Kitchen.API.Tests.Integration.TestAuthHandler</c>, and
/// <c>Discount.Grpc.Tests.Integration.TestAuthHandler</c>. The
/// <see cref="Discount.Grpc.Tests.Integration.TestAuthHandler"/>
/// variant also emits a stable <c>restaurantId</c> claim so the
/// <see cref="BuildingBlocks.Multitenancy.ClaimsRestaurantProvider"/>
/// resolves the tenant for the request; this handler follows the same
/// pattern. The plan §6 Phase 5 drift item recorded this as a
/// deliberate deviation from the plan's literal
/// <c>JwtTestAuthenticationHandler</c> shape.</para>
/// <para>The stable restaurant id is hardcoded to
/// <c>11111111-1111-1111-1111-111111111111</c>; the matching
/// test-side id used by <c>BasketSeedHelper.SeedBasketAsync</c> is
/// the same. Endpoint tests that need a per-test restaurant id must
/// populate the <c>restaurantId</c> claim via a custom
/// <see cref="ClaimsPrincipal"/> — not currently used by Phase 5.</para>
/// </remarks>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Scheme name registered in the test host.</summary>
    public const string SchemeName = "Test";

    /// <summary>
    /// Stable restaurant id baked into every test principal. The Marten
    /// <c>MultiTenanted()</c> filter reads the same id from the
    /// <see cref="BuildingBlocks.Multitenancy.ClaimsRestaurantProvider"/>
    /// so seeded baskets and request-scoped queries land in the same
    /// tenant partition.
    /// </summary>
    public const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";

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
            // Stable restaurant claim so the global query filter is
            // satisfied for tests that need tenant scoping. The claim
            // name matches `BuildingBlocks.Multitenancy.ClaimsRestaurantProvider`
            // which reads `restaurantId` literally.
            new("restaurantId", TestRestaurantId),
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
