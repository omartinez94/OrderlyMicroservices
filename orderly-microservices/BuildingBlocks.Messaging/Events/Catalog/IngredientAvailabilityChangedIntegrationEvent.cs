namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by Catalog's Ingredient Availability Engine (Phase 3) when a
/// menu item's ingredient-derived availability flips between
/// <c>Available</c>, <c>Limited</c>, or <c>Unavailable</c>.
/// </summary>
/// <remarks>
/// <para>Contract defined in Phase 2. Phase 3 is the only publisher.
/// Basket and Ordering consume this to re-validate pending baskets and
/// reject new orders whose status is <c>Unavailable</c> (per
/// <c>CATALOG_SERVICE_PLAN.md</c> §6.5).</para>
/// </remarks>
public record IngredientAvailabilityChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>The menu item whose derived availability changed.</summary>
    public Guid MenuItemId { get; init; }

    /// <summary>Tenant scope for downstream filtering.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>
    /// Serialized <c>BuildingBlocks.Enums.AvailabilityStatus</c>
    /// (<c>"Available"</c> | <c>"Limited"</c> | <c>"Unavailable"</c>).
    /// </summary>
    public string AvailabilityStatus { get; init; } = default!;

    /// <summary>
    /// When the engine resolves a single auto-substitute alternative and
    /// <c>Restaurant.AllowAutoSubstitute</c> is <see langword="true"/>, this
    /// carries the alternative ingredient id. <see langword="null"/>
    /// otherwise (status flips without an auto-substitute).
    /// </summary>
    public Guid? AutoSubstituteOf { get; init; }
}