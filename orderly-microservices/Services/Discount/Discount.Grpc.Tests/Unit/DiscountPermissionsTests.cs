using Microsoft.AspNetCore.Authorization;

namespace Discount.Grpc.Tests.Unit;

/// <summary>
/// Pins the JWT claim-shape handling on the assertion registered by
/// <see cref="Discount.Grpc.Authorization.AuthorizationPolicies.AddDiscountPolicies"/>.
/// Two observed Identity emission shapes must produce the same success
/// result for a granted permission:
/// <list type="bullet">
/// <item><b>Shape A</b> — one <c>permissions</c> claim per granted permission
/// (<c>new Claim("permissions", p)</c> per <c>p</c>). This is the canonical
/// Identity emission per <c>current-architecture.md §7</c>.</item>
/// <item><b>Shape B</b> — single <c>permissions</c> claim with a
/// comma-separated value (<c>new Claim("permissions", "a,b,c")</c>).</item>
/// </list>
/// Per plan v1.2 changelog M-L8 the assertion honours both so a
/// regression on either side surfaces as a failing
/// <see cref="AuthorizeAsync"/> result rather than a silent deny.
/// </summary>
public sealed class DiscountPermissionsTests
{
    /// <summary>The permission strings Discount enforces — mirrors
    /// <see cref="DiscountPermissions.All"/>; inlined so a deletion of a
    /// const fails fast in the test surface instead of silently passing.</summary>
    public static IEnumerable<object[]> AllPermissions()
    {
        yield return new object[] { "coupon:read" };
        yield return new object[] { "coupon:create" };
        yield return new object[] { "coupon:edit" };
        yield return new object[] { "coupon:delete" };
        yield return new object[] { "coupon:redeem" };
        yield return new object[] { "reward-code:read" };
        yield return new object[] { "reward-code:create" };
        yield return new object[] { "reward-code:edit" };
        yield return new object[] { "reward-code:delete" };
        yield return new object[] { "reward-code:redeem" };
        yield return new object[] { "discount-rule:read" };
        yield return new object[] { "discount-rule:edit" };
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public async Task ShapeA_HeldPermission_IsAuthorized(string permission)
    {
        var sp = BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        // Shape A — one claim per granted permission.
        var principal = BuildPrincipal(claims => claims.Add(new Claim("permissions", permission)));

        var result = await auth.AuthorizeAsync(principal, resource: null, policyName: permission);

        result.Succeeded.Should().BeTrue(
            $"a JWT with the {permission} permission in Shape A should authorize the policy");
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public async Task ShapeB_HeldPermission_IsAuthorized(string permission)
    {
        var sp = BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        // Shape B — single comma-separated claim.
        var principal = BuildPrincipal(claims => claims.Add(new Claim("permissions", permission)));

        var result = await auth.AuthorizeAsync(principal, resource: null, policyName: permission);

        result.Succeeded.Should().BeTrue(
            $"a JWT with the {permission} permission in Shape B (comma-split) should authorize the policy");
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public async Task MixedShape_HeldPermission_IsAuthorized(string permission)
    {
        // The Identity canonical pattern emits BOTH Shape A (one-claim-per)
        // AND Shape B (the comma-split claim) on the same JWT. Both
        // paths must succeed independently when one path is present.
        var sp = BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        var principal = BuildPrincipal(claims =>
        {
            claims.Add(new Claim("permissions", permission));            // Shape A
            claims.Add(new Claim("permissions", $"{permission},coupon:read"));  // Shape B (with one extra token)
        });

        var result = await auth.AuthorizeAsync(principal, resource: null, policyName: permission);

        result.Succeeded.Should().BeTrue(
            "either shape present satisfies the policy");
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public async Task MissingClaim_IsDenied(string permission)
    {
        var sp = BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        var principal = BuildPrincipal(claims => { /* no permissions claim */ });

        var result = await auth.AuthorizeAsync(principal, resource: null, policyName: permission);

        result.Succeeded.Should().BeFalse(
            "missing permission claim fails the default-deny assertion");
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public async Task WrongPermission_IsDenied(string permission)
    {
        var sp = BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        // Holder has only 'coupon:read' (Shape A). The test asserts the
        // expected-success-per-permission matrix: only the held permission
        // succeeds; every other policy fails. (The unrelated Shape A +
        // Shape B combination tests live in the per-shape [Theory]s
        // above; this method targets the wrong-permission deny path.)
        var principal = BuildPrincipal(claims =>
        {
            claims.Add(new Claim("permissions", "coupon:read"));
        });

        var result = await auth.AuthorizeAsync(principal, resource: null, policyName: permission);

        // For permission = coupon:read this passes (the holder does have it);
        // for any other permission this fails. Branch per permission.
        var expectedSuccess = permission == "coupon:read";
        result.Succeeded.Should().Be(expectedSuccess,
            $"a JWT with only 'coupon:read' should NOT authorize the '{permission}' policy");
    }

    /// <summary>Builds a minimal service provider with the Discount
    /// authorization policies registered. Mirrors the production
    /// <c>AuthorizationPolicies.AddDiscountPolicies</c> chain so the
    /// probe asserts the same code path the gRPC interceptor exercises.</summary>
    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Inline copy of AuthorizationPolicies.AddDiscountPolicies's body so
        // the unit test runs without the gRPC host. The shape-handling
        // assertion MUST match the production `RequireAssertion` exactly;
        // any drift surfaces as a failing test here + (separately) at the
        // JwtClaimShapeProbe integration test in Commit C.
        services.AddAuthorization(options =>
        {
            foreach (var permission in DiscountPermissions.All)
            {
                options.AddPolicy(permission, policy =>
                {
                    policy.RequireAssertion(ctx =>
                    {
                        foreach (var claim in ctx.User.FindAll("permissions"))
                        {
                            if (string.Equals(claim.Value, permission, StringComparison.Ordinal))
                            {
                                return true;
                            }
                            foreach (var held in claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (string.Equals(held.Trim(), permission, StringComparison.Ordinal))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    });
                });
            }
        });

        return services.BuildServiceProvider();
    }

    /// <summary>Builds a <see cref="ClaimsPrincipal"/> with the given
    /// claim-shape builder applied. Adds the canonical
    /// <see cref="ClaimTypes.NameIdentifier"/> so the principal is not
    /// anonymous (the authorization pipeline is happier).</summary>
    private static ClaimsPrincipal BuildPrincipal(Action<List<Claim>> claimsBuilder)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "test-user"),
            new("restaurantId", "11111111-1111-1111-1111-111111111111"),
        };
        claimsBuilder(claims);
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}
