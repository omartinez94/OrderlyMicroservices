using System.Reflection;
using Discount.Grpc.Services;

namespace Discount.Grpc.Authorization;

/// <summary>
/// Registers the Discount-specific authorization stack on an <see cref="IServiceCollection"/>:
///   - Twelve claim-gated authorization policies (one per <see cref="DiscountPermissions.All"/> entry).
///   - The <see cref="DiscountAuthorizationInterceptor"/> as a singleton, with the
///     method-path → permission map pre-computed by reflection over every
///     Discount gRPC service class (<see cref="DiscountService"/>,
///     <see cref="DiscountRuleService"/>, <see cref="RewardCodeService"/>).
///
/// Note: we intentionally do NOT call <c>BuildingBlocks.Authorization.AddAuthorizationServices</c>
/// here — that registers the dormant <c>PermissionPolicyProvider</c> + <c>PermissionAuthorizationHandler</c>
/// which Discount doesn't use (<c>RequireAssertion</c> owns the
/// claim-shape contract going forward, with the hardening as a future revision).
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// The gRPC service classes whose <c>[Permission]</c>-attributed
    /// methods are reflected into the method-path → permission map.
    /// All three live under <c>Discount.Grpc.Services</c>; adding a
    /// fourth means adding it here so the new methods get enforced
    /// alongside the rest.
    /// </summary>
    private static readonly Type[] GrpcServiceTypes =
    {
        typeof(DiscountService),
        typeof(DiscountRuleService),
        typeof(RewardCodeService),
    };

    public static IServiceCollection AddDiscountPolicies(this IServiceCollection services)
    {
        // Build the method-path → permission map at startup so the interceptor stays O(1) per call.
        var methodMap = BuildMethodPermissionMap();

        services.AddSingleton(new MethodPermissionMap(methodMap));
        services.AddSingleton<DiscountAuthorizationInterceptor>();

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

    /// <summary>
    /// Walks every gRPC service class in <see cref="GrpcServiceTypes"/>,
    /// resolves the gRPC wire-format service name from each class's
    /// protobuf-generated outer container (the <c>__ServiceName</c>
    /// static field), and emits one <c>/{serviceName}/{MethodName}</c>
    /// → <c>permission</c> entry per <c>[Permission]</c>-attributed
    /// method. The map's keys match the value of
    /// <see cref="Grpc.Core.ServerCallContext.Method"/> at call time.
    /// </summary>
    /// <remarks>
    /// Why <c>__ServiceName</c> and not <c>BaseType.DeclaringType.FullName</c>:
    /// the latter returns the C# namespace path (<c>Discount.Grpc.DiscountProtoService</c>),
    /// but the gRPC framework sets <c>context.Method</c> to the wire-format
    /// service name (<c>discount.DiscountProtoService</c>). The two differ in
    /// casing and in the package prefix the .proto file declares. Reading
    /// <c>__ServiceName</c> via reflection is the only way to keep the map
    /// in lockstep with the wire without hard-coding a string per service.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> BuildMethodPermissionMap()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var serviceType in GrpcServiceTypes)
        {
            var wireServiceName = ResolveWireServiceName(serviceType);

            var methods = serviceType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<PermissionAttribute>() is not null);

            foreach (var method in methods)
            {
                var permission = method.GetCustomAttribute<PermissionAttribute>()!.Permission;
                var methodPath = $"/{wireServiceName}/{method.Name}";
                if (result.TryGetValue(methodPath, out var existing) && existing != permission)
                {
                    throw new InvalidOperationException(
                        $"Method path '{methodPath}' is registered with two different permissions " +
                        $"('{existing}' and '{permission}'). Check the [Permission] attributes on " +
                        $"{serviceType.Name}.{method.Name}.");
                }
                result[methodPath] = permission;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the protobuf-generated <c>__ServiceName</c> static field off
    /// the outer class that contains the gRPC base. The path is:
    /// concrete service class → its gRPC base class → the base's
    /// declaring type (the static container class, e.g.
    /// <c>DiscountProtoService</c>) → <c>__ServiceName</c>.
    /// </summary>
    private static string ResolveWireServiceName(Type concreteServiceType)
    {
        var baseType = concreteServiceType.BaseType
            ?? throw new InvalidOperationException(
                $"{concreteServiceType.Name} must inherit a gRPC service base class.");

        var serviceContainer = baseType.DeclaringType
            ?? throw new InvalidOperationException(
                $"The gRPC base for {concreteServiceType.Name} ({baseType.FullName}) has no declaring type — " +
                "is the partial class layout still the protobuf-generated one?");

        var serviceNameField = serviceContainer.GetField("__ServiceName",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"The gRPC container class {serviceContainer.FullName} does not expose a __ServiceName " +
                "static field — was the Grpc.Tools version bumped or the .proto regenerated?");

        return (string)serviceNameField.GetValue(null)!;
    }
}
