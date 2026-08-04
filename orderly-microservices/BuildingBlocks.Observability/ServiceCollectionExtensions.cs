using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability;

/// <summary>
/// OpenTelemetry registration extensions for the Orderly service
/// collection. The single canonical entry point is
/// <see cref="AddOrderlyOpenTelemetry"/>; services that need log
/// shipping also call <c>LoggingBuilderExtensions.AddOrderlyOpenTelemetry</c>
/// (see <see cref="LoggingBuilderExtensions"/>).
/// </summary>
/// <remarks>
/// <para>
/// Per the PERSISTENCE_AND_RELIABILITY_PLAN §0.2 guard rail "services do
/// not call <c>AddOpenTelemetry()</c> directly", every service in the
/// solution funnels through this method. The only OpenTelemetry
/// <c>PackageReference</c>s in the solution live in this project; service
/// csprojs only add a <c>ProjectReference</c>.
/// </para>
/// <para>
/// <b>Ordering.</b> This method MUST be called before
/// <c>AddControllers()</c> / <c>AddCarter()</c> / <c>AddGrpc()</c> so the
/// activity source is registered before the request pipeline is built
/// (PERSISTENCE_AND_RELIABILITY_PLAN §10.5).
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenTelemetry pipeline: ASP.NET Core + HttpClient +
    /// Marten + MassTransit + Npgsql tracing, ASP.NET Core + HttpClient +
    /// runtime metrics, and a shared <see cref="ResourceBuilder"/>
    /// emitted on every signal. The OTLP exporter is wired when
    /// <see cref="ObservabilityOptions.Enabled"/> is <c>true</c>.
    /// </summary>
    /// <param name="services">The service collection being extended.</param>
    /// <param name="configuration">
    /// Configuration root used to bind the <c>OpenTelemetry</c> section.
    /// The <c>OpenTelemetry__Endpoint</c> env var is the canonical way
    /// to point the OTLP exporter at the collector in containerised
    /// deploys.
    /// </param>
    /// <param name="serviceName">
    /// Service name emitted on every span + metric as the
    /// <c>service.name</c> resource attribute. Required.
    /// </param>
    /// <param name="serviceVersion">
    /// Optional service version override. Defaults to the executing
    /// assembly's <see cref="System.Reflection.AssemblyVersion"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOrderlyOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string? serviceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var section = configuration.GetSection(ObservabilityOptions.SectionName);
        services.AddOptions<ObservabilityOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var version = serviceVersion
            ?? options.ServiceVersion
            ?? typeof(ServiceCollectionExtensions).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var resolvedServiceName = options.ServiceName ?? serviceName;

        var resource = ResourceBuilder
            .CreateEmpty()
            .AddOrderlyService(resolvedServiceName, version);

        // The exporter is only attached when OpenTelemetry:Enabled is true.
        // When false, the in-process Activity pipeline still runs so
        // Activity.Current is populated for any test that wants to assert
        // on spans, but no outbound HTTP/2 traffic is generated.
        var otlpEndpoint = options.Enabled ? new Uri(options.Endpoint) : null;
        var otlpProtocol = options.OtlpProtocol == OtlpProtocol.HttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Marten")
                    .AddSource("MassTransit")
                    .AddNpgsql();
                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = otlpEndpoint;
                        o.Protocol = otlpProtocol;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(o =>
                    {
                        o.Endpoint = otlpEndpoint;
                        o.Protocol = otlpProtocol;
                    });
                }
            })
            .WithLogging(logging =>
            {
                logging.SetResourceBuilder(resource);
                if (otlpEndpoint is not null && options.LogsEnabled)
                {
                    logging.AddOtlpExporter(o =>
                    {
                        o.Endpoint = otlpEndpoint;
                        o.Protocol = otlpProtocol;
                    });
                }
            });

        return services;
    }
}
