namespace Kitchen.API.Domain.Events;

/// <summary>
/// Carrier passed from <c>OrderCreatedIntegrationEventHandler</c> into
/// <c>KitchenTicket.CreateFromOrder</c>. Captures only the kitchen-relevant
/// fields per item so the domain layer does not depend on the integration
/// event type. Variations / customizations arrive as the typed
/// <see cref="BuildingBlocks.Messaging.Events.KitchenOrderItemVariation"/>
/// / <see cref="BuildingBlocks.Messaging.Events.KitchenOrderItemCustomization"/>
/// records on the integration event (Phase D) and are flattened to
/// <c>IReadOnlyList&lt;string&gt;</c> by
/// <c>KitchenTicketExtensions.ToOrderItemSeeds</c> before the seed is
/// constructed — the aggregate's jsonb columns keep the
/// <c>string[]</c> shape, so the schema is unchanged.
/// </summary>
public record OrderItemSeed(
    Guid OrderItemId,
    Guid MenuItemId,
    string MenuItemName,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyList<string> SelectedVariations,
    IReadOnlyList<string> Customizations,
    string? SpecialInstructions,
    int? SeatNumber);