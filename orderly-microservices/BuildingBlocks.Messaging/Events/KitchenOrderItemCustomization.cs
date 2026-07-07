namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Free-form customer customization carried on
/// <see cref="KitchenOrderItemPreview.Customizations"/>. Carries the
/// <c>Label</c> (e.g. "<c>No onions</c>") plus an optional <c>Value</c>
/// (e.g. "<c>Spicy</c>") and <c>Price</c> delta so the kitchen display
/// renders the instruction exactly as the customer entered it.
/// </summary>
public record KitchenOrderItemCustomization(
    string Label,
    string? Value,
    decimal? Price);