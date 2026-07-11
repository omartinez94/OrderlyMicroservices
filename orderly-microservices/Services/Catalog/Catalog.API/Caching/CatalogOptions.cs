using System.ComponentModel.DataAnnotations;

namespace Catalog.API.Caching;

/// <summary>
/// Strongly-typed configuration for the catalog cache subsystem. Bound from the
/// <c>Catalog</c> section of <c>appsettings.json</c> in <c>Program.cs</c>.
/// Validated at startup via <c>ValidateOnStart()</c> so misconfiguration fails fast.
/// </summary>
public sealed class CatalogOptions
{
    /// <summary>
    /// Configuration section name for <see cref="CatalogOptions"/>.
    /// </summary>
    public const string SectionName = "Catalog";

    /// <summary>
    /// Interval in minutes between drift-repair ticks. The hosted service
    /// (<see cref="CacheDriftRepairService"/>) re-populates missing menu cache
    /// keys for every restaurant in the database.
    /// </summary>
    /// <remarks>Allowed range: 1 to 1440 (1 minute to 24 hours).</remarks>
    [Range(1, 1440)]
    public int CacheRepairIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Time-to-live in minutes for the <c>catalog:menu:{rid}</c> cache entry.
    /// </summary>
    /// <remarks>Allowed range: 1 to 1440 (1 minute to 24 hours).</remarks>
    [Range(1, 1440)]
    public int MenuCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Time-to-live in minutes for the <c>catalog:ingredients:{rid}</c> cache entry.
    /// Populated by Phase 3.
    /// </summary>
    /// <remarks>Allowed range: 1 to 1440 (1 minute to 24 hours).</remarks>
    [Range(1, 1440)]
    public int IngredientCacheTtlMinutes { get; set; } = 5;
}