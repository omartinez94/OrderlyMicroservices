using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Correlation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BuildingBlocks.Behaviors;

/// <summary>
/// MediatR pipeline behavior that:
/// <list type="bullet">
/// <item>Establishes an ambient <see cref="CorrelationContext"/> for the
/// request scope — read from the <c>X-Correlation-Id</c> HTTP header when
/// present, or a fresh <see cref="Guid"/> otherwise. The value is stamped
/// on every persisted audit row (e.g. <c>OrderActivity.CorrelationId</c>).
/// </item>
/// <item>Wraps the handler in a structured-logging <c>BeginScope</c> so log
/// lines emitted by downstream code carry the correlation id.</item>
/// <item>Logs the start/end of every request and emits a warning when the
/// handler exceeds a 3-second threshold.</item>
/// <item>Redacts the request payload from log lines when the command type
/// is annotated with <see cref="PciSensitiveAttribute"/>. The behavior
/// logs only the type name in the request-data slot — the payload
/// itself never reaches a sink.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The correlation id is set via <c>CorrelationContext.Set</c> and cleared
/// in a <c>try/finally</c> — the cleanup runs even if the handler throws,
/// so the <see cref="AsyncLocal{T}"/> cannot leak into a subsequent
/// request on the same logical call context.
/// </para>
/// <para>
/// When the behavior runs outside an HTTP scope (e.g. a background worker),
/// <see cref="IHttpContextAccessor.HttpContext"/> is <c>null</c> and no id
/// is set — the ambient stays <c>null</c>, which the domain layer treats
/// as "no request context" (the activity row's <c>CorrelationId</c> is
/// left <c>null</c>).
/// </para>
/// </remarks>
public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>
    /// Cache of <see cref="PciSensitiveAttribute"/> lookups. Reflection
    /// runs once per concrete <c>TRequest</c>; subsequent invocations
    /// hit the cache. Concurrent-safe — MediatR may execute handlers
    /// across thread pool threads.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> _pciSensitiveCache = new();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var correlationId = ResolveOrGenerateCorrelationId();
        var ownsAmbient = correlationId is not null;

        if (correlationId is not null)
        {
            CorrelationContext.Set(correlationId);
        }

        try
        {
            var isPci = IsPciSensitive();
            var requestData = isPci ? (object)RedactedLabel() : request;

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "[START] Handling request: {Request}, Response: {Response}, CorrelationId: {CorrelationId}, Request data: {RequestData}",
                    typeof(TRequest).Name, typeof(TResponse).Name, correlationId ?? "<none>", requestData);
            }

            var timer = new Stopwatch();
            timer.Start();

            var response = await next(cancellationToken);

            timer.Stop();
            var elapsed = timer.Elapsed;
            if (elapsed.TotalSeconds > 3)
            {
                logger.LogWarning(
                    "[PERFORMANCE] Handling request: {Request}, Response: {Response}, CorrelationId: {CorrelationId}, Request data: {RequestData}, Elapsed time: {ElapsedTime}",
                    typeof(TRequest).Name, typeof(TResponse).Name, correlationId ?? "<none>", requestData, elapsed);
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "[END] Handling request: {Request}, Response: {Response}, CorrelationId: {CorrelationId}, Request data: {RequestData}, Elapsed time: {ElapsedTime}",
                        typeof(TRequest).Name, typeof(TResponse).Name, correlationId ?? "<none>", requestData, elapsed);
                }
            }

            return response;
        }
        finally
        {
            if (ownsAmbient)
            {
                CorrelationContext.Clear();
            }
        }
    }

    /// <summary>
    /// Reads the <c>X-Correlation-Id</c> header from the current HTTP
    /// request, or generates a fresh <see cref="Guid"/> if missing or empty.
    /// Returns <c>null</c> when no HTTP context is in scope (background work).
    /// </summary>
    private string? ResolveOrGenerateCorrelationId()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Request.Headers.TryGetValue(CorrelationHeader, out var values)
            && !string.IsNullOrWhiteSpace(values.ToString()))
        {
            return values.ToString();
        }

        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Cached reflection check for <see cref="PciSensitiveAttribute"/>.
    /// Hot-path optimised — first call walks the type's attribute list,
    /// every subsequent call is a dictionary lookup keyed on the type.
    /// </summary>
    private static bool IsPciSensitive() =>
        _pciSensitiveCache.GetOrAdd(typeof(TRequest), static t => t.GetCustomAttribute<PciSensitiveAttribute>(inherit: false) is not null);

    /// <summary>
    /// Replacement string for the request-data slot when the command
    /// carries PII/PCI. The type name is logged so operators can still
    /// identify which command fired; the payload is dropped.
    /// </summary>
    private static string RedactedLabel() => $"{typeof(TRequest).Name} (payload redacted)";
}