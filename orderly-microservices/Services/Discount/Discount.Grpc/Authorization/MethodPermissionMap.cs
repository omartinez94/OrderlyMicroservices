namespace Discount.Grpc.Authorization;

/// <summary>
/// Lookup table from full gRPC method path (e.g.
/// <c>/Discount.Grpc.DiscountProtoService/GetDiscount</c>) to the permission
/// required to invoke it. Methods without a <see cref="PermissionAttribute"/>
/// on the gRPC service class are intentionally absent — health checks and
/// similar bypass the gate.
/// </summary>
/// <remarks>
/// Built at startup by reflecting on <see cref="Services.DiscountService"/>'s
/// <c>[Permission]</c>-attributed methods (see
/// <see cref="AuthorizationPolicies.AddDiscountPolicies"/>). Injected as a
/// singleton into <see cref="DiscountAuthorizationInterceptor"/> for O(1)
/// per-call lookup.
/// </remarks>
public sealed record MethodPermissionMap(IReadOnlyDictionary<string, string> ByServiceMethod)
{
    public string? FindPermission(string fullMethod) =>
        ByServiceMethod.TryGetValue(fullMethod, out var permission) ? permission : null;
}
