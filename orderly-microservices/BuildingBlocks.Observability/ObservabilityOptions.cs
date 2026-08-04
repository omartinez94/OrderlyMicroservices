using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Observability;

/// <summary>
/// Configuration POCO bound from the <c>OpenTelemetry</c> section of
/// <c>appsettings.json</c> by <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>.
/// </summary>
/// <remarks>
/// The same options class governs all three OpenTelemetry signals
/// (traces, metrics, logs). When <see cref="Enabled"/> is <c>false</c> the
/// pipeline still registers the activity sources + logger provider so
/// <c>Activity.Current</c> is populated and the in-process logger provider
/// is available — but no OTLP exporter is wired. This is the contract the
/// test factories rely on (<c>OpenTelemetry:Enabled=false</c> in
/// <c>appsettings.Test.json</c>).
/// </remarks>
public sealed class ObservabilityOptions
{
    /// <summary>Configuration section name used by the binder.</summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Master switch. When <c>false</c> the OpenTelemetry pipeline is
    /// registered but no OTLP exporters are wired — useful in tests where
    /// the OTLP collector is unavailable. When <c>true</c> the host
    /// publishes traces + metrics + logs to the configured
    /// <see cref="Endpoint"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// OTLP endpoint (e.g. <c>http://localhost:4317</c> for gRPC or
    /// <c>http://localhost:4318</c> for HTTP/protobuf). Required because
    /// the OTel SDK default is a localhost fallback that fails in
    /// containerised deploys. The <c>OpenTelemetry__Endpoint</c> env var
    /// maps to this key via the .NET EnvironmentVariables provider.
    /// </summary>
    [Required]
    public string Endpoint { get; init; } = "http://localhost:4317";

    /// <summary>
    /// Optional fallback service name. The <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>
    /// method takes the service name as a required argument; this property
    /// exists for backward compatibility with the Basket pre-Phase-4
    /// <c>OtelOptions</c> shape and for services that bind configuration
    /// before calling the extension.
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Optional service version surfaced as the <c>service.version</c>
    /// resource attribute. Defaults to the assembly version when the
    /// <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>
    /// <c>serviceVersion</c> argument is not supplied.
    /// </summary>
    public string? ServiceVersion { get; init; }

    /// <summary>
    /// Toggles the OTLP log exporter. Defaults to <c>true</c>; set to
    /// <c>false</c> to keep the in-process logger provider registered
    /// (so <c>ILogger</c> calls still flow through the SDK) while
    /// disabling outbound log shipping. Mirrors the trace/metric
    /// <see cref="Enabled"/> contract for symmetry.
    /// </summary>
    public bool LogsEnabled { get; init; } = true;

    /// <summary>
    /// OTLP transport protocol. Defaults to <c>Grpc</c> (the OTel
    /// SDK's default, port 4317). Set to <c>HttpProtobuf</c> (port
    /// 4318) when the collector is fronted by an HTTP-only
    /// adapter or when running under test with an in-process Kestrel
    /// receiver that exposes <c>/v1/traces</c>, <c>/v1/metrics</c>,
    /// <c>/v1/logs</c>. Case-insensitive; both <c>grpc</c> and
    /// <c>GRPC</c> are accepted.
    /// </summary>
    public OtlpProtocol OtlpProtocol { get; init; } = OtlpProtocol.Grpc;
}

/// <summary>
/// OTLP transport protocol. Mirrors
/// <see cref="OpenTelemetry.Exporter.OtlpExportProtocol"/> without
/// leaking the OpenTelemetry assembly into every service's
/// configuration model.
/// </summary>
public enum OtlpProtocol
{
    /// <summary>gRPC OTLP transport (default; collector port 4317).</summary>
    Grpc = 0,

    /// <summary>HTTP/protobuf OTLP transport (collector port 4318).</summary>
    HttpProtobuf = 1,
}
