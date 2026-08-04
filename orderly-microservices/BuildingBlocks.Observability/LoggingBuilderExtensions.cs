using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;

namespace BuildingBlocks.Observability;

/// <summary>
/// LoggingBuilder extension that wires the OpenTelemetry logger
/// provider + OTLP log exporter. Service hosts call this once, alongside
/// <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>,
/// so the log signal flows through the same OTLP endpoint as traces
/// and metrics.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds the OpenTelemetry <see cref="ILoggerProvider"/> so log
    /// records become <c>LogRecord</c>s in the same pipeline that
    /// carries traces and metrics. When
    /// <see cref="ObservabilityOptions.Enabled"/> is <c>false</c> the
    /// in-process logger provider is still registered (so log calls
    /// still flow through the SDK); only the outbound OTLP exporter
    /// is skipped. When <see cref="ObservabilityOptions.LogsEnabled"/>
    /// is <c>false</c> the OTLP exporter is skipped even with
    /// <see cref="ObservabilityOptions.Enabled"/> set.
    /// </summary>
    /// <param name="builder">The logging builder being extended.</param>
    /// <param name="configuration">
    /// Configuration root used to bind the <c>OpenTelemetry</c> section.
    /// </param>
    /// <returns>The same <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddOrderlyOpenTelemetry(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var otlpProtocol = options.OtlpProtocol == OtlpProtocol.HttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        // The OpenTelemetryLoggerProvider is registered unconditionally
        // so the in-process SDK still buffers log records (mirroring the
        // trace/metric behaviour). The OTLP exporter is gated on Enabled
        // + LogsEnabled.
        builder.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (options.Enabled && options.LogsEnabled)
            {
                logging.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(options.Endpoint);
                    o.Protocol = otlpProtocol;
                });
            }
        });

        return builder;
    }
}
