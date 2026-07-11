namespace Catalog.API.Readers;

/// <summary>
/// Top-level snapshot of a restaurant's menu tree. Returned by
/// <see cref="IMenuReader.GetByRestaurantAsync"/> and cached as JSON under the
/// <c>catalog:menu:{restaurantId}</c> key.
/// </summary>
/// <remarks>
/// Returned by the read-side assembly; not an EF Core entity. The structure is
/// deliberately flat (no <c>.Include</c> chains in the API surface) so it
/// serialises stably to JSON and survives schema migrations without cache
/// busts that would be triggered by navigation-property renames.
/// </remarks>
public sealed record MenuSnapshot(
    Guid RestaurantId,
    Instant SnapshotAt,
    IReadOnlyList<MenuCategoryNode> Categories)
{
    /// <summary>
    /// Returns <see langword="true"/> when the snapshot has no categories
    /// (an empty or fully-soft-deleted menu). Consumers may treat this as a
    /// "restaurant not yet onboarded" signal.
    /// </summary>
    public bool IsEmpty => Categories.Count == 0;
}

/// <summary>
/// A menu category with its sub-categories inlined. Populated by
/// <see cref="Readers.MenuReader"/>.
/// </summary>
public sealed record MenuCategoryNode(
    int Id,
    string Name,
    string Description,
    int DisplayOrder,
    IReadOnlyList<MenuSubCategoryNode> SubCategories);

/// <summary>
/// A menu sub-category with its items inlined.
/// </summary>
public sealed record MenuSubCategoryNode(
    int Id,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive,
    IReadOnlyList<MenuItemNode> Items);

/// <summary>
/// A menu item with its variations and ingredient requirements inlined.
/// </summary>
/// <param name="AvailabilityStatus">
/// Plain string to keep the cache schema-independent from the
/// <see cref="BuildingBlocks.Enums.AvailabilityStatus"/> enum. Phase 3's
/// Ingredient Availability Engine writes the value; until then the field
/// reflects the persisted <c>MenuItem.AvailabilityStatus</c> directly.
/// Possible values: <c>"Available"</c>, <c>"Limited"</c>, <c>"Unavailable"</c>.
/// </param>
public sealed record MenuItemNode(
    Guid Id,
    string Name,
    string Description,
    decimal BasePrice,
    decimal? PromoPrice,
    bool IsAvailable,
    string AvailabilityStatus,
    string ItemType,
    int DisplayOrder,
    int PrepTimeMinutes,
    int PrepTimeMaxMinutes,
    string ImageUrl,
    IReadOnlyList<MenuItemVariationNode> Variations,
    IReadOnlyList<MenuItemIngredientNode> Ingredients);

/// <summary>
/// A menu item variation (e.g. "Large", "Extra Spicy") with its price modifier.
/// </summary>
public sealed record MenuItemVariationNode(
    int Id,
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder);

/// <summary>
/// A menu-item-ingredient requirement. Used by the engine (Phase 3) to
/// compute availability; exposed here so downstream services (Basket, Ordering)
/// can render "contains allergens" / "auto-substitute" hints without a
/// second round-trip.
/// </summary>
public sealed record MenuItemIngredientNode(
    int Id,
    int IngredientId,
    decimal QuantityRequired,
    bool IsOptional);