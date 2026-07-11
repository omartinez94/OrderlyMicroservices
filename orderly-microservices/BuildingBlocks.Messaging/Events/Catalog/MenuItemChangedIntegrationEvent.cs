namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by <c>Catalog.API</c> when a menu item (or anything that
/// affects the menu tree — categories, sub-categories, variations,
/// combo items, item-ingredient links) is created, updated, or deleted.
/// </summary>
/// <remarks>
/// <see cref="ChangeType"/> discriminates the action. Optional payload
/// fields are populated on Created / Updated; Deleted publishes the
/// identifier only (so consumers can invalidate cache + delete related
/// data without reading the dead item back).
/// </remarks>
public record MenuItemChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>The menu item whose state changed.</summary>
    public Guid MenuItemId { get; init; }

    /// <summary>Tenant scope for downstream filtering / cache invalidation.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>Discriminator — Created / Updated / Deleted.</summary>
    public MenuItemChangeType ChangeType { get; init; }

    /// <summary>Item name. <see langword="null"/> on Delete.</summary>
    public string? Name { get; init; }

    /// <summary>Base price snapshot at mutation time. <see langword="null"/> on Delete.</summary>
    public decimal? BasePrice { get; init; }

    /// <summary>Availability flag. <see langword="null"/> on Delete.</summary>
    public bool? IsAvailable { get; init; }

    /// <summary>
    /// Serialized <c>BuildingBlocks.Enums.AvailabilityStatus</c>
    /// (<c>"Available"</c> | <c>"Limited"</c> | <c>"Unavailable"</c>). <see langword="null"/> on Delete.
    /// Phase 3's engine writes the value; mutation handlers echo the
    /// current persisted value for now.
    /// </summary>
    public string? AvailabilityStatus { get; init; }
}

/// <summary>Discriminator for <see cref="MenuItemChangedIntegrationEvent"/>.</summary>
public enum MenuItemChangeType
{
    Created,
    Updated,
    Deleted,
}