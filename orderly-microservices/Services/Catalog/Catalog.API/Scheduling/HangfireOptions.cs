using System.ComponentModel.DataAnnotations;

namespace Catalog.API.Scheduling;

/// <summary>
/// Strongly-typed configuration for the Hangfire-backed scheduled jobs in
/// <c>Catalog.API/Scheduling/</c>. Bound from the
/// <c>Catalog:Hangfire</c> section of <c>appsettings.json</c> in
/// <c>Program.cs</c>. Validated at startup via <c>ValidateOnStart()</c>
/// so misconfiguration fails fast.
/// </summary>
/// <remarks>
/// The four recurring jobs share a single feature-flag gate
/// (<c>FeatureManagement__CatalogScheduledJobs</c>). The per-job intervals
/// here control Hangfire's <see cref="Hangfire.RecurringJob"/> cadence —
/// they tick every minute even when the flag is off (Hangfire is unaware
/// of the feature flag), and each job's <c>Run</c> method early-exits
/// when the flag is disabled. This is the same self-gating pattern as
/// <see cref="CacheDriftRepairService"/>.
/// </remarks>
public sealed class HangfireOptions
{
    /// <summary>Configuration section name for <see cref="HangfireOptions"/>.</summary>
    public const string SectionName = "Catalog:Hangfire";

    /// <summary>Cron expression for <see cref="ReservationReminderJob"/>.</summary>
    /// <remarks>Default: every 5 minutes. Standard 5-field cron.</remarks>
    public string ReservationReminderCron { get; set; } = "*/5 * * * *";

    /// <summary>Cron expression for <see cref="ReservationNoShowJob"/>.</summary>
    /// <remarks>Default: every minute.</remarks>
    public string ReservationNoShowCron { get; set; } = "* * * * *";

    /// <summary>Cron expression for <see cref="WalkInNoShowJob"/>.</summary>
    /// <remarks>Default: every minute.</remarks>
    public string WalkInNoShowCron { get; set; } = "* * * * *";

    /// <summary>Cron expression for <see cref="SeasonalAvailabilityJob"/>.</summary>
    /// <remarks>Default: every 5 minutes.</remarks>
    public string SeasonalAvailabilityCron { get; set; } = "*/5 * * * *";

    /// <summary>
    /// Maximum rows a single tick of any of the four jobs is allowed to
    /// process. Bounds the time each tick can hold a transaction open.
    /// </summary>
    /// <remarks>Default 500. Allowed range: 1 to 100_000.</remarks>
    [Range(1, 100_000)]
    public int MaxRowsPerTick { get; set; } = 500;

    /// <summary>
    /// Number of Hangfire worker threads used to process the recurring
    /// jobs. <c>0</c> would disable the worker pool; the catalog service
    /// only runs four jobs, so a small pool is fine.
    /// </summary>
    /// <remarks>Default 4. Allowed range: 1 to 64.</remarks>
    [Range(1, 64)]
    public int WorkerCount { get; set; } = 4;
}