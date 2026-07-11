namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// One selected variation carried on <see cref="KitchenOrderItemPreview.SelectedVariations"/>.
/// Records the <c>Name</c> (e.g. "<c>Size: Large</c>") and the <c>Price</c>
/// delta the variation adds to the line item so the kitchen display and
/// the bill agree on the breakdown.
/// </summary>
public record KitchenOrderItemVariation(string Name, decimal Price);