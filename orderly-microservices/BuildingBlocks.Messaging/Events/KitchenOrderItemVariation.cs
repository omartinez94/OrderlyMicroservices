namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Catalog-driven size / variant selection carried on
/// <see cref="KitchenOrderItemPreview.SelectedVariations"/>. Pairs the
/// variant <c>Name</c> with its <c>Price</c> delta so the kitchen display
/// can render "<c>Size: Large (+$2.50)</c>" without an extra lookup.
/// </summary>
public record KitchenOrderItemVariation(string Name, decimal Price);