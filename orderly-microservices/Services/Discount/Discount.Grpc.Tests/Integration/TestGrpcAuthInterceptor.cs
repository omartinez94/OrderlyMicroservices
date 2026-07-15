using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// gRPC server interceptor that reads test-auth metadata on each call and
/// synthesises a <see cref="ClaimsPrincipal"/> on the underlying
/// <see cref="HttpContext"/>. ASP.NET Core gRPC 2.x does NOT promote
/// arbitrary client <c>Metadata</c> entries to <c>HttpContext.Request.Headers</c>
/// by default (only well-known headers like <c>Authorization</c> flow through),
/// so the <see cref="TestAuthHandler"/> sees nothing in production tests.
/// This interceptor closes that gap — first-of-kind pattern for gRPC
/// integration tests in the monorepo.
/// </summary>
/// <remarks>
/// <para>
/// Reads two metadata keys per call:
/// </para>
/// <list type="bullet">
/// <item><c>x-test-user</c> — Guid identifying the caller.</item>
/// <item><c>x-test-permissions</c> — comma-separated permission strings.</item>
/// </list>
/// <para>
/// Mirrors <see cref="TestAuthHandler"/>'s claim shape so the global
/// <see cref="DiscountAuthorizationInterceptor"/> on the server reads the
/// same principal it would have via the JWT bearer path in production.
/// </para>
/// </remarks>
public sealed class TestGrpcAuthInterceptor : Interceptor
{
    public const string UserMetadataKey = "x-test-user";
    public const string PermissionsMetadataKey = "x-test-permissions";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ApplyPrincipal(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyPrincipal(context);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyPrincipal(context);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyPrincipal(context);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads <see cref="UserMetadataKey"/> + <see cref="PermissionsMetadataKey"/>
    /// from <paramref name="context"/>'s metadata and synthesises a
    /// <see cref="ClaimsPrincipal"/> on the per-call <see cref="HttpContext"/>.
    /// Falls through silently when neither key is set (the call then
    /// continues into production auth — <see cref="TestAuthHandler"/>'s
    /// <c>NoResult</c> on a JWT-less request).
    /// </summary>
    private static void ApplyPrincipal(ServerCallContext context)
    {
        var userId = context.RequestHeaders.Get(UserMetadataKey)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var parsedUserId))
        {
            return;
        }

        var permissionsRaw = context.RequestHeaders.Get(PermissionsMetadataKey)?.Value ?? string.Empty;
        var permissions = string.IsNullOrWhiteSpace(permissionsRaw)
            ? Array.Empty<string>()
            : permissionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, parsedUserId.ToString()),
            new(ClaimTypes.Name, $"test-user-{parsedUserId}"),
            // Stable tenant claim so the global query filter lines up
            // with the in-prod restaurant claim.
            new("restaurantId", "11111111-1111-1111-1111-111111111111"),
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permissions", permission));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        var principal = new ClaimsPrincipal(identity);

        // Reach through HttpContext to set the principal. ASP.NET Core
        // gRPC exposes the underlying HttpContext per call via
        // ServerCallContext.GetHttpContext() — explicit set on User
        // lets the subsequent AuthorizeAsync see the test principal.
        var httpContext = context.GetHttpContext();
        httpContext.User = principal;
    }
}
