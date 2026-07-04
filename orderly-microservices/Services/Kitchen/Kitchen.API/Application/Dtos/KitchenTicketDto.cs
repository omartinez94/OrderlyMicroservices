namespace Kitchen.API.Application.Dtos;

/// <summary>
/// Read-side projection of a <c>KitchenTicket</c> + its items. Returned by
/// <c>GET /api/v1/kitchen/queue</c> and <c>GET /api/v1/kitchen/tickets/{id}</c>.
/// </summary>
public record KitchenTicketDto(
    Guid Id,
    Guid RestaurantId,
    Guid CustomerId,
    string OrderNumber,
    KitchenTicketStatus Status,
    Instant ReceivedAt,
    Instant? StartedAt,
    Instant? ReadyAt,
    Instant? BumpedAt,
    Instant? CancelledAt,
    string? CancellationReason,
    Guid? ConfirmedByUserId,
    Guid? CancelledByUserId,
    string Notes,
    int EstimatedPrepTimeMinutes,
    IReadOnlyList<KitchenTicketItemDto> Items);

public record KitchenTicketItemDto(
    Guid Id,
    Guid OrderItemId,
    Guid MenuItemId,
    string MenuItemName,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyList<string> SelectedVariations,
    IReadOnlyList<string> Customizations,
    string? SpecialInstructions,
    int? SeatNumber,
    KitchenItemStatus Status,
    Instant? StartedAt,
    Instant? ReadyAt,
    Guid? StationId);

public record KitchenStationDto(
    Guid Id,
    Guid RestaurantId,
    string Name,
    int SortOrder,
    bool IsActive);