namespace Catalog.API.Caching;

/// <summary>
/// Single source of truth for catalog cache key formats. All Catalog cache keys
/// are namespaced under <c>catalog:*</c> so other services sharing the same Redis
/// instance (Basket, etc.) cannot collide.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Cache key for the full menu snapshot of a restaurant.
    /// Format: <c>catalog:menu:{restaurantId}</c> where <c>{restaurantId}</c> is the
    /// GUID's 32-character "N" format (no hyphens) — saves 12 bytes per key.
    /// </summary>
    public static string Menu(Guid restaurantId) => $"catalog:menu:{restaurantId:N}";

    /// <summary>
    /// Cache key for the ingredient availability snapshot of a restaurant.
    /// Format: <c>catalog:ingredients:{restaurantId}</c>.
    /// Populated by Phase 3's Ingredient Availability Engine; the Phase 1
    /// invalidation hook already removes it on ingredient mutations.
    /// </summary>
    public static string Ingredients(Guid restaurantId) => $"catalog:ingredients:{restaurantId:N}";
}