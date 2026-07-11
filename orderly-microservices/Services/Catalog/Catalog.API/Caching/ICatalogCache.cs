namespace Catalog.API.Caching;

/// <summary>
/// Invalidation-only abstraction over the catalog cache. Mutation handlers
/// inject this — not <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// directly — so cache-key formatting stays encapsulated in <see cref="CacheKeys"/>
/// and every call is best-effort (fail-open).
/// </summary>
public interface ICatalogCache
{
    /// <summary>
    /// Removes the cached menu snapshot for a restaurant. Called by every
    /// handler that mutates the menu tree (categories, subcategories, items,
    /// variations, combo items).
    /// </summary>
    /// <param name="restaurantId">The owning restaurant's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Never throws. Failures are logged at <c>Warning</c> level. The
    /// drift-repair hosted service (<c>CacheDriftRepairService</c>) is the
    /// safety net.
    /// </remarks>
    Task InvalidateMenuAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached ingredient availability snapshot for a restaurant.
    /// Called by every handler that mutates ingredients, ingredient alternatives,
    /// or the menu-item-ingredient junction. The snapshot itself is populated by
    /// Phase 3's Ingredient Availability Engine.
    /// </summary>
    /// <param name="restaurantId">The owning restaurant's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Never throws. Failures are logged at <c>Warning</c> level.</remarks>
    Task InvalidateIngredientsAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}