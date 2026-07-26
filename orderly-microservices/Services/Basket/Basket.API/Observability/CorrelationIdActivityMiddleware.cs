using System.Diagnostics;

namespace Basket.API.Observability;

/// <summary>
/// ASP.NET Core middleware that bridges the inbound
/// <c>X-Correlation-Id</c> header into the OpenTelemetry
/// <see cref="Activity.Current"/> bag so every emitted span carries
/// the same id as the <see cref="LoggingBehavior{TRequest,TResponse}"/>
/// <c>BeginScope</c> log line.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in Basket rather than BuildingBlocks.</b>
/// The header name and tag name are operator-tunable and
/// per-service (Basket could legitimately use
/// <c>X-Basket-Correlation-Id</c> instead). Keeping the middleware
/// next to the OpenTelemetry wiring means a future migration to a
/// new tag scheme does not require touching every service.
/// </para>
/// <para>
/// <b>Header behaviour.</b> If the inbound request has an
/// <c>X-Correlation-Id</c> header, the value is used as the
/// <c>correlation_id</c> Activity tag. Otherwise a fresh
/// <see cref="Guid.NewGuid"/> is generated. Either way the
/// outbound response carries the header so downstream callers can
/// correlate. The middleware does NOT take a dependency on
/// <c>CorrelationContext</c> for read-back — that's a
/// <c>BuildingBlocks</c> concern wired in the MediatR pipeline.
/// </para>
/// </remarks>
public sealed class CorrelationIdActivityMiddleware(RequestDelegate next)
{
    /// <summary>HTTP header name read on the inbound request and echoed on the response.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>OpenTelemetry Activity tag name used for the correlation id.</summary>
    public const string TagName = "correlation_id";

    public async Task InvokeAsync(HttpContext context)
    {
        // Read or mint the correlation id. Honour an inbound
        // header; mint a fresh GUID otherwise. The same value is
        // used three places: the response header (caller can
        // correlate), the OTel Activity tag (the trace can
        // correlate), and CorrelationContext.Current (the log line
        // can correlate).
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var inbound) && !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString()
            : Guid.NewGuid().ToString();

        // Stamp the Activity. When no Activity exists (the request
        // is below AspNetCore's tracing threshold) this is a no-op.
        Activity.Current?.SetTag(TagName, correlationId);

        // Echo back to the caller. Set BEFORE `next` so the
        // response header is in flight even if the handler throws
        // — the global exception handler will then attach the
        // ProblemDetails body without disturbing the header.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
