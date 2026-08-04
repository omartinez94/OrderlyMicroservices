using OpenTelemetry.Resources;

namespace BuildingBlocks.Observability;

/// <summary>
/// Helper extensions for shaping the OpenTelemetry <see cref="ResourceBuilder"/>
/// used by every signal (traces, metrics, logs).
/// </summary>
public static class ResourceBuilderExtensions
{
    /// <summary>
    /// Adds the Orderly-standard resource attributes to the supplied
    /// <paramref name="builder"/>: <c>service.name</c>,
    /// <c>service.version</c>, and <c>service.instance.id</c>. The
    /// instance id is <see cref="Environment.MachineName"/> by default
    /// because every Orderly service runs in a uniquely-named container.
    /// </summary>
    /// <param name="builder">Existing <see cref="ResourceBuilder"/>.</param>
    /// <param name="serviceName">
    /// Value for the <c>service.name</c> resource attribute. The
    /// OpenTelemetry semantic conventions recommend a stable,
    /// lowercase, dot-separated identifier (e.g. <c>orderly.catalog</c>).
    /// </param>
    /// <param name="serviceVersion">
    /// Value for the <c>service.version</c> resource attribute.
    /// </param>
    /// <returns>The same <see cref="ResourceBuilder"/> for chaining.</returns>
    public static ResourceBuilder AddOrderlyService(
        this ResourceBuilder builder,
        string serviceName,
        string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceVersion);
        return builder
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName);
    }
}
