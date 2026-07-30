using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Dev;

/// <summary>
/// Maps a dev-only HTTP trigger endpoint guarded by a shared secret header.
/// Use to expose background-job triggers, fixture loaders, or other internal
/// surface area to the <c>Orderly.DevMCP.Server</c> companion without
/// opening the same surface to production traffic.
/// </summary>
/// <remarks>
/// <para><b>Caller responsibility:</b> registration must be wrapped in
/// <c>if (app.Environment.IsDevelopment()) { app.MapDevTriggerEndpoint(...); }</c>
/// so production hosts never register the dev surface. The extension
/// itself doesn't try to read <see cref="IHostEnvironment"/> at
/// registration time — the host environment isn't always resolved by
/// then, and forcing <see cref="IServiceProvider"/> materialization in
/// a builder block is a known anti-pattern.</para>
/// <para><b>One gate at request time:</b> the <c>X-Dev-Trigger-Secret</c>
/// header value is compared against <c>DEV_TRIGGER_SECRET</c> using
/// <see cref="CryptographicOperations.FixedTimeEquals"/> so a timing
/// attack can't recover the secret byte-by-byte. The
/// <c>X-Dev-Trigger-Source</c> header is logged (informational) but is
/// not required.</para>
/// </remarks>
public static class DevTriggerEndpointExtensions
{
    /// <summary>
    /// Header the caller must send with the shared dev secret. Compared
    /// against <c>DEV_TRIGGER_SECRET</c> env var via constant-time
    /// equality.
    /// </summary>
    public const string SecretHeader = "X-Dev-Trigger-Secret";

    /// <summary>
    /// Optional header logging the source of the trigger call (e.g.
    /// <c>orderly-devmcp</c>). Logged but not required.
    /// </summary>
    public const string SourceHeader = "X-Dev-Trigger-Source";

    /// <summary>
    /// Environment variable that holds the shared secret compared
    /// against <see cref="SecretHeader"/>. Must be set on the API
    /// host at request time or the endpoint returns 503.
    /// </summary>
    public const string SecretEnvVar = "DEV_TRIGGER_SECRET";

    /// <summary>
    /// Map a dev-only POST endpoint at <paramref name="path"/>. The
    /// caller is responsible for gating on
    /// <c>app.Environment.IsDevelopment()</c>.
    /// </summary>
    /// <param name="endpoints">The route builder to extend.</param>
    /// <param name="path">
    /// The literal request path (no template — the MCP server calls
    /// known fixed paths like <c>/_dev/trigger/clear-abandoned-baskets</c>).
    /// </param>
    /// <param name="handler">
    /// The endpoint delegate. Invoked only after the dev-secret gate
    /// has accepted the request.
    /// </param>
    /// <returns>The same <paramref name="endpoints"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapDevTriggerEndpoint(
        this IEndpointRouteBuilder endpoints,
        string path,
        Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(handler);

        var route = endpoints.MapPost(path, handler)
            .WithName($"DevTrigger:{path}");

        route.WithMetadata(new DevTriggerEndpointAttribute(path));

        return endpoints;
    }

    /// <summary>
    /// Validates the request's <see cref="SecretHeader"/> against the
    /// <see cref="SecretEnvVar"/> env var using constant-time equality.
    /// Writes 503 (secret env unset) or 401 (header mismatch) on
    /// rejection so the response shape distinguishes the two failure
    /// modes for diagnostics.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the request should proceed to the handler;
    /// <c>false</c> when the response has already been written.
    /// </returns>
    public static async Task<bool> ValidateSecretAsync(
        HttpContext ctx,
        CancellationToken cancellationToken)
    {
        var loggerFactory = ctx.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger("BuildingBlocks.Dev");

        var secret = Environment.GetEnvironmentVariable(SecretEnvVar);
        if (string.IsNullOrEmpty(secret))
        {
            logger?.LogWarning(
                "Dev trigger rejected — {Env} env var is not set on the host.",
                SecretEnvVar);
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync(
                $"Dev trigger requires {SecretEnvVar} on the host.",
                cancellationToken);
            return false;
        }

        if (!ctx.Request.Headers.TryGetValue(SecretHeader, out var presented)
            || string.IsNullOrEmpty(presented))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync(
                $"Missing {SecretHeader} header.",
                cancellationToken);
            return false;
        }

        var presentedBytes = Encoding.UTF8.GetBytes(presented.ToString());
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (!CryptographicOperations.FixedTimeEquals(presentedBytes, secretBytes))
        {
            logger?.LogWarning(
                "Dev trigger rejected — {Header} header did not match {Env}.",
                SecretHeader,
                SecretEnvVar);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync(
                $"{SecretHeader} header did not match.",
                cancellationToken);
            return false;
        }

        var source = ctx.Request.Headers.TryGetValue(SourceHeader, out var src)
            ? src.ToString()
            : "(unset)";
        logger?.LogInformation(
            "Dev trigger accepted — path={Path} source={Source}",
            ctx.Request.Path,
            source);

        return true;
    }
}

/// <summary>
/// Marker attribute on routes registered through
/// <see cref="DevTriggerEndpointExtensions.MapDevTriggerEndpoint"/> so
/// OpenAPI generators can skip them and so test introspection can
/// enumerate the dev surface without scanning route names.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DevTriggerEndpointAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}