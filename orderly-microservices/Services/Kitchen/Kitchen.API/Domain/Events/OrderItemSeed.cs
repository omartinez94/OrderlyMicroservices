namespace Kitchen.API.Domain.Events;

/// <summary>
/// Carrier passed from <c>OrderCreatedIntegrationEventHandler</c> into
/// <c>KitchenTicket.CreateFromOrder</c>. Captures only the kitchen-relevant
/// fields per item so the domain layer does not depend on the integration
/// event type.
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