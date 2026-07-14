using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Authorization;

namespace Discount.Grpc.Authorization;

/// <summary>
/// Server-side gRPC interceptor that enforces per-method permission policies.
/// Looks up the required permission from the injected <see cref="MethodPermissionMap"/>
/// (built at startup by reflecting on <see cref="Services.DiscountService"/>'s
/// <c>[Permission]</c> attributes), runs <see cref="IAuthorizationService"/> for
/// the current <see cref="System.Security.Claims.ClaimsPrincipal"/> on the call's
/// <c>HttpContext</c>, and rejects the call with <see cref="StatusCode.PermissionDenied"/>
/// + a <c>required-permission</c> trailer on failure.
///
/// Why a global interceptor: <c>[Authorize(Policy=...)]</c> on a gRPC service
/// method is silently ignored — gRPC services aren't routed through the MVC pipeline.
/// The interceptor is the project's actual mechanism.
/// </summary>
public sealed class DiscountAuthorizationInterceptor(MethodPermissionMap methodMap) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var permission = methodMap.FindPermission(context.Method);
        if (permission is not null)
        {
            var httpContext = context.GetHttpContext()
                ?? throw new InvalidOperationException(
                    "HttpContext unavailable on gRPC call; AddJwtBearer must be wired in Program.cs.");

            var authz = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
            var result = await authz.AuthorizeAsync(httpContext.User, resource: null, policyName: permission);
            if (!result.Succeeded)
            {
                var trailers = new Metadata
                {
                    { "required-permission", permission },
                };
                throw new RpcException(
                    new Status(StatusCode.PermissionDenied, $"Missing permission: {permission}"),
                    trailers);
            }
        }

        return await continuation(request, context);
    }
}
