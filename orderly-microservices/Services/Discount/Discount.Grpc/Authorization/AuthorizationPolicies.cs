using System.Reflection;
using Discount.Grpc.Services;

namespace Discount.Grpc.Authorization;

/// <summary>
/// Registers the Discount-specific authorization stack on an <see cref="IServiceCollection"/>:
///   - Twelve claim-gated authorization policies (one per <see cref="DiscountPermissions.All"/> entry).
///   - The <see cref="DiscountAuthorizationInterceptor"/> as a singleton, with the
///     method-path → permission map pre-computed by reflection on <see cref="DiscountService"/>.
///
/// Note: we intentionally do NOT call <c>BuildingBlocks.Authorization.AddAuthorizationServices</c>
/// here — that registers the dormant <c>PermissionPolicyProvider</c> + <c>PermissionAuthorizationHandler</c>
/// which Discount doesn't use (<c>RequireAssertion</c> owns the
/// claim-shape contract going forward, with the hardening as a future revision).
/// </summary>
public static class AuthorizationPolicies
{
    public static IServiceCollection AddDiscountPolicies(this IServiceCollection services)
    {
        // Build the method-path → permission map at startup so the interceptor stays O(1) per call.
        var methodMap = typeof(DiscountService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<PermissionAttribute>() is not null)
            .ToDictionary(
                m => $"/Discount.Grpc.DiscountProtoService/{m.Name}",
                m => m.GetCustomAttribute<PermissionAttribute>()!.Permission);

        services.AddSingleton(new MethodPermissionMap(methodMap));

        services.AddAuthorization(options =>
        {
            foreach (var permission in DiscountPermissions.All)
            {
                options.AddPolicy(permission, policy =>
                {
                    policy.RequireAssertion(ctx =>
                    {
                        // Identity emits permissions in either Shape A (one claim per
                        // granted permission, `c.Value == permission`) or Shape B (a
                        // single claim with a comma-separated value, `c.Value ==
                        // "a,b,c"`). `JwtClaimShapeProbe` integration
                        // test locks the actual emission shape at runtime; this
                        // assertion honours both so a regression on either side
                        // surfaces as a failing policy assertion rather than a
                        // silent deny. Default-deny: when the claim is absent
                        // or doesn't include the required permission on either
                        // shape, the policy fails.
                        foreach (var claim in ctx.User.FindAll("permissions"))
                        {
                            if (string.Equals(claim.Value, permission, StringComparison.Ordinal))
                            {
                                return true; // Shape A: whole-claim equality.
                            }
                            foreach (var held in claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (string.Equals(held.Trim(), permission, StringComparison.Ordinal))
                                {
                                    return true; // Shape B: comma-separated value.
                                }
                            }
                        }
                        return false;
                    });
                });
            }
        });

        return services;
    }
}
