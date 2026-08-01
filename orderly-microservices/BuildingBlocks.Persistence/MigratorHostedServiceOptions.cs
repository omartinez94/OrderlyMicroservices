namespace BuildingBlocks.Persistence;

/// <summary>
/// Tunables for <see cref="MigratorHostedService{TContext}"/>. Bound from
/// the host configuration section named <see cref="SectionName"/> via
/// <c>Configure&lt;MigratorHostedServiceOptions&gt;(builder.Configuration.GetSection(SectionName))</c>.
/// </summary>
/// <remarks>
/// <para>The defaults are tuned for cold-start Postgres: 2s initial backoff,
/// doubling each attempt up to 32s, capped at <see cref="MigrationTimeoutSeconds"/>
/// = 120s of total wall-clock. That covers the typical "DB still warming up"
/// window while failing fast enough to let Kubernetes restart the pod before
/// the readiness probe blackholes the replica.</para>
/// <para>Disable the migrator entirely by setting <see cref="Enabled"/> = false.
/// Use this for canary deploys where migrations are operator-applied via a
/// separate pipeline before the rolling restart begins.</para>
/// </remarks>
public sealed class MigratorHostedServiceOptions
{
    public const string SectionName = "Migrator";

    /// <summary>Master switch. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum retry attempts before failing the host. 1-100. Default: 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>First backoff after a transient failure. Seconds. Default: 2.</summary>
    public double InitialBackoffSeconds { get; set; } = 2.0;

    /// <summary>Cap on the exponential backoff. Seconds. Default: 32.</summary>
    public double MaxBackoffSeconds { get; set; } = 32.0;

    /// <summary>Multiplier applied to the backoff each attempt. Default: 2.0.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Hard wall-clock cap. After this many seconds, the migrator stops
    /// retrying and throws — the host fails fast. Default: 120.
    /// </summary>
    public int MigrationTimeoutSeconds { get; set; } = 120;
}