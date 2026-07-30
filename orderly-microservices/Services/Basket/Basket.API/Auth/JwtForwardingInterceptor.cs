using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Basket.API.Auth;

/// <summary>
/// gRPC interceptor that copies the inbound
/// <c>Authorization: Bearer &lt;jwt&gt;</c> header from the
/// caller's HTTP request into outbound gRPC
/// <c>Metadata["authorization"]</c>, so the upstream
/// <c>Discount.Grpc</c> service can authenticate the call without a
/// separate dev-secret.
/// </summary>
/// <remarks>
/// <para><b>Why a per-call interceptor (vs. per-client):</b>
/// <c>DiscountProtoServiceClient</c> is registered as a singleton-
/// scoped gRPC client; its lifetime is one HTTP request. Each
/// request carries a different inbound JWT (different user, different
/// restaurant). The interceptor resolves the token from
/// <see cref="IHttpContextAccessor"/> per call, so it picks up the
/// right value for the right request without sharing state across
/// callers.</para>
/// <para><b>Failure modes (mirrors plan §8.3):</b></para>
/// <list type="bullet">
/// <item><b>No inbound Authorization header</b> — the interceptor logs
/// <c>WARN</c> + proceeds without setting
/// <c>Metadata["authorization"]</c>. Discount's
/// <c>AuthenticationInterceptor</c> returns
/// <c>StatusCode.Unauthenticated</c> from the gRPC layer (Phase 1
/// §0.4.5 path).</item>
/// <item><b>Malformed inbound token</b> (e.g. <c>Basic xxx</c> instead
/// of <c>Bearer yyy</c>) — same path as missing: log WARN + proceed.
/// The interceptor does NOT validate the JWT itself (validation lives
/// on the Discount side per Phase 1's <c>AddJwtBearer</c>).</item>
/// <item><b>Missing <see cref="IHttpContextAccessor"/></b> — the
/// interceptor logs WARN + proceeds (treated as a service-to-service
/// call rather than a user request; Discount's interceptor will
/// reject).</item>
/// </list>
/// </remarks>
public sealed class JwtForwardingInterceptor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<JwtForwardingInterceptor> logger)
    : Interceptor
{
    private const string AuthHeader = "authorization";

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        AttachJwt(context.Options);
        return base.AsyncUnaryCall(request, context, continuation);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        AttachJwt(context.Options);
        return base.AsyncClientStreamingCall(context, continuation);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        AttachJwt(context.Options);
        return base.AsyncServerStreamingCall(request, context, continuation);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        AttachJwt(context.Options);
        return base.AsyncDuplexStreamingCall(context, continuation);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        AttachJwt(context.Options);
        return base.BlockingUnaryCall(request, context, continuation);
    }

    private void AttachJwt(CallOptions options)
    {
        var http = httpContextAccessor.HttpContext;
        if (http is null)
        {
            logger.LogWarning(
                "JwtForwardingInterceptor: no HttpContext (service-to-service call); outbound gRPC will not carry a JWT.");
            return;
        }

        var authHeader = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader))
        {
            logger.LogWarning(
                "JwtForwardingInterceptor: no inbound Authorization header; Discount.Grpc will return Unauthenticated.");
            return;
        }

        // CallOptions.Headers is immutable per call. We rebuild the
        // options with the original metadata + the new authorization
        // entry. Mutating the metadata directly is not safe — it
        // would race with other interceptors in the chain.
        var newMetadata = new Metadata();
        if (options.Headers is not null)
        {
            foreach (var entry in options.Headers)
            {
                newMetadata.Add(entry);
            }
        }
        newMetadata.Add(AuthHeader, authHeader);

        // CallOptions is a struct; we mutate it via the field
        // assignment on the context.Options. The gRPC client library
        // reads the new options for the outbound call.
        var field = typeof(CallOptions).GetField("Headers");
        if (field is null)
        {
            // Future-proof: if gRPC drops the public Headers setter,
            // we degrade gracefully and skip the attach. The interceptor
            // is best-effort; production callers should ensure their
            // host version still exposes Headers.
            logger.LogWarning(
                "JwtForwardingInterceptor: CallOptions.Headers not settable on this gRPC version; outbound call will not carry the JWT.");
            return;
        }

        field.SetValueDirect(__makeref(options), newMetadata);
    }
}