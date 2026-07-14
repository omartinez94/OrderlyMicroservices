namespace Discount.Grpc.Authorization;

/// <summary>
/// Marks a gRPC service method with the permission required to invoke it.
/// The <see cref="DiscountAuthorizationInterceptor"/> reflects on the concrete
/// <c>DiscountService</c>'s methods at startup to build a method-path → permission
/// map; a missing attribute is treated as "no permission required" (intentional
/// for future public health checks and admin tooling that bypass claim checks).
/// Use <see cref="DiscountPermissions"/> constants — never raw strings — so the
/// rename / cross-service sync hygiene is enforced by the compiler.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
