using BuildingBlocks.Messaging.Events;

namespace Ordering.Application.Dtos;

public record OrderItemDto(
    Guid Id,
    Guid OrderId,
    Guid MenuItemId,
    string MenuItemName,
    string MenuItemDescription,
    string MenuItemImageUrl,
    int Quantity,
    decimal UnitPrice,
    decimal BasePrice,
    decimal TotalPrice,
    int SeatNumber,
    string SpecialInstructions,
    IReadOnlyList<KitchenOrderItemCustomization> Customizations,
    IReadOnlyList<KitchenOrderItemVariation> SelectedVariations,
    PrepStatus PrepStatus,
    Instant CreatedAt,
    Instant? PrepStartedAt,
    Instant? PrepCompletedAt
);