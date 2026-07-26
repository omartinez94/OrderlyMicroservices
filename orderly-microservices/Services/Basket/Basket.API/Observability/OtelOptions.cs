using System.ComponentModel.DataAnnotations;

namespace Basket.API.Observability;

/// <summary>
/// Configuration for the OpenTelemetry pipeline. Bound from the
/// <c>OpenTelemetry</c> section of <c>appsettings.json</c>; the
/// <c>Endpoint</c> value is the OTLP gRPC collector address
/// (e.g. <c>http://localhost:4317</c>) the host exports to.
/// </summary>
public sealed class OtelOptions
{
    /// <summary>Configuration section name used by the binder.</summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Master switch. When <c>false</c> the OpenTelemetry pipeline
    /// is registered but no exporters are wired — useful in tests
    /// where the OTLP collector is unavailable. When <c>true</c>
    /// the host publishes traces + metrics to the configured
    /// <see cref="Endpoint"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// OTLP gRPC endpoint (e.g. <c>http://localhost:4317</c>). When
    /// null the pipeline still builds but emits no spans — the host
    /// falls back to <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var if
    /// present, mirroring the OpenTelemetry SDK convention.
    /// </summary>
    [Required]
    public string Endpoint { get; init; } = "http://localhost:4317";

    /// <summary>
    /// Service name emitted on every span + metric as the
    /// <c>service.name</c> resource attribute. Default
    /// <c>basket.api</c> matches the container name.
    /// </summary>
    public string ServiceName { get; init; } = "basket.api";

    /// <summary>
    /// Service version emitted on every span + metric. Default
    /// <c>1.0.0</c> — bump on every release tag.
    /// </summary>
    public string ServiceVersion { get; init; } = "1.0.0";
}
