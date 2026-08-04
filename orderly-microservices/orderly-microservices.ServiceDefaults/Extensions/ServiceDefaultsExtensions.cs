using BuildingBlocks.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OrderlyMicroservices.ServiceDefaults.Extensions;

/// <summary>
/// Shared <see cref="WebApplicationBuilder"/> wiring for every Orderly
/// service. Mirrors the .NET Aspire <c>AddServiceDefaults</c> pattern:
/// one extension, one call site per service, one consistent observability
/// + health-checks baseline.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Registers the OpenTelemetry pipeline (traces + metrics + logs) via
    /// <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>
    /// and <see cref="LoggingBuilderExtensions.AddOrderlyOpenTelemetry"/>,
    /// plus the standard <see cref="IHealthChecksBuilder"/>. The
    /// <c>Microsoft.Extensions.Http.Resilience</c> package is referenced
    /// transitively so <c>AddStandardResilienceHandler()</c> resolves
    /// (used by Basket's gRPC client).
    /// </summary>
    /// <param name="builder">The web application builder being extended.</param>
    /// <param name="serviceName">
    /// Value for the <c>service.name</c> resource attribute. The shared
    /// convention is <c>Orderly.&lt;Service&gt;</c> (e.g.
    /// <c>Orderly.Catalog</c>).
    /// </param>
    /// <returns>The same <see cref="WebApplicationBuilder"/> for chaining.</returns>
    public static WebApplicationBuilder AddOrderlyDefaults(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, serviceName);
        builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);
        builder.Services.AddHealthChecks();

        return builder;
    }
}
