namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published when an image for a menu item is confirmed in object storage.
/// Used by downstream consumers (e.g. search indexing, thumbnail processing).
/// </summary>
public record MenuItemImageUploadedIntegrationEvent : IntegrationEvent
{
    public Guid RestaurantId { get; init; }
    public Guid MenuItemId { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string ObjectKey { get; init; } = string.Empty;
}
