using System.Text.Json;

namespace Catalog.API.Caching;

/// <summary>
/// Redis-backed cache-on-read decorator for <see cref="IMenuReader"/>.
/// Registered via Scrutor as
/// <c>services.AddScoped&lt;IMenuReader, MenuReader&gt;().Decorate&lt;IMenuReader, CachedMenuReader&gt;()</c>
/// in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// <para><b>Failure policy.</b> Every <see cref="IDistributedCache"/> call is
/// best-effort: Redis failures are logged at <c>Warning</c> level and the call
/// falls through to the inner reader. Cache outages never fail writes or reads.</para>
/// <para><b>Null result.</b> When the inner reader returns <see langword="null"/>
/// (no menu for the restaurant), no cache entry is written — the result is not
/// stored because a future menu onboarding would otherwise be masked by a
/// stale negative cache entry.</para>
/// <para><b>JSON shape.</b> Serialised with <see cref="JsonSerializerDefaults.Web"/>
/// plus <c>PropertyNamingPolicy = null</c> so the round-trip matches the API
/// contract (PascalCase, same as <c>Program.cs</c>).</para>
/// </remarks>
public sealed class CachedMenuReader : IMenuReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    private readonly IMenuReader inner;
    private readonly IDistributedCache cache;
    private readonly IOptionsMonitor<CatalogOptions> options;
    private readonly ILogger<CachedMenuReader> logger;

    /// <summary>
    /// Constructs the decorator. The inner <see cref="IMenuReader"/> must be
    /// resolvable from the same scope (it is — both are <c>Scoped</c>).
    /// </summary>
    public CachedMenuReader(
        IMenuReader inner,
        IDistributedCache cache,
        IOptionsMonitor<CatalogOptions> options,
        ILogger<CachedMenuReader> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.inner = inner;
        this.cache = cache;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MenuSnapshot?> GetByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.Menu(restaurantId);

        // 1. Cache lookup — fail-open on Redis errors.
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cached))
            {
                var hit = JsonSerializer.Deserialize<MenuSnapshot>(cached, SerializerOptions);
                if (hit is not null)
                {
                    logger.LogDebug("Menu cache hit for restaurant {RestaurantId}", restaurantId);
                    return hit;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Menu cache read failed for restaurant {RestaurantId}; falling through to source.",
                restaurantId);
        }

        // 2. Miss → inner reader.
        var snapshot = await inner.GetByRestaurantAsync(restaurantId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        // 3. Populate cache — fail-open on Redis errors.
        try
        {
            var ttl = TimeSpan.FromMinutes(options.CurrentValue.MenuCacheTtlMinutes);
            var payload = JsonSerializer.Serialize(snapshot, SerializerOptions);
            await cache.SetStringAsync(
                key,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken).ConfigureAwait(false);
            logger.LogDebug(
                "Menu cache populated for restaurant {RestaurantId} with TTL {TtlMinutes}m",
                restaurantId,
                options.CurrentValue.MenuCacheTtlMinutes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Menu cache write failed for restaurant {RestaurantId}; serving uncached result.",
                restaurantId);
        }

        return snapshot;
    }
}