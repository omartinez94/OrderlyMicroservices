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
                        // Permissions arrive under the "permissions" claim as a comma-separated
                        // string (canonical Identity emission shape). The assertion splits,
                        // trims, and check for exact equality. Default-deny: when the claim is
                        // absent or doesn't include the required permission, the policy fails.
                        // Per plan §3 "Default deny on missing claim".
                        foreach (var claim in ctx.User.FindAll("permissions"))
                        {
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

        return services;
    }
}
