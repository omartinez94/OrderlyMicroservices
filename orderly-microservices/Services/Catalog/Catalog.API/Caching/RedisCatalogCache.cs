namespace Catalog.API.Caching;

/// <summary>
/// Redis-backed implementation of <see cref="ICatalogCache"/>. Every call is
/// best-effort: Redis failures are logged at <c>Warning</c> level and swallowed
/// so the calling mutation handler's transaction commits regardless of cache
/// health. The drift-repair hosted service is the safety net.
/// </summary>
/// <remarks>
/// Registered as <c>Singleton</c> because <see cref="IDistributedCache"/> is
/// thread-safe and stateless across calls (the underlying connection multiplexer
/// is owned by the framework).
/// </remarks>
public sealed class RedisCatalogCache(
    IDistributedCache cache,
    ILogger<RedisCatalogCache> logger) : ICatalogCache
{
    /// <inheritdoc/>
    public Task InvalidateMenuAsync(Guid restaurantId, CancellationToken cancellationToken = default) =>
        RemoveBestEffortAsync(CacheKeys.Menu(restaurantId), label: "menu", cancellationToken);

    /// <inheritdoc/>
    public Task InvalidateIngredientsAsync(Guid restaurantId, CancellationToken cancellationToken = default) =>
        RemoveBestEffortAsync(CacheKeys.Ingredients(restaurantId), label: "ingredients", cancellationToken);

    private async Task RemoveBestEffortAsync(string key, string label, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Cache invalidation failed for {Label} key {Key}; drift-repair service will repopulate.",
                label,
                key);
        }
    }
}