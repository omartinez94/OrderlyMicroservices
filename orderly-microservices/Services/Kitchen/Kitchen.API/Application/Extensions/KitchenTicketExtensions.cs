namespace Kitchen.API.Application.Extensions;

public static class KitchenTicketExtensions
{
    public static KitchenTicketDto ToDto(this KitchenTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return new KitchenTicketDto(
            Id: ticket.Id.Value,
            RestaurantId: ticket.RestaurantId,
            CustomerId: ticket.CustomerId,
            OrderNumber: ticket.OrderNumber,
            Status: ticket.Status,
            ReceivedAt: ticket.ReceivedAt,
            StartedAt: ticket.StartedAt,
            ReadyAt: ticket.ReadyAt,
            BumpedAt: ticket.BumpedAt,
            CancelledAt: ticket.CancelledAt,
            CancellationReason: ticket.CancellationReason,
            ConfirmedByUserId: ticket.ConfirmedByUserId,
            CancelledByUserId: ticket.CancelledByUserId,
            Notes: ticket.Notes,
            EstimatedPrepTimeMinutes: ticket.Items.Count > 0
                ? ticket.Items.Count * 5 // placeholder until prep-time tracking lands
                : 0,
            Items: ticket.Items.Select(i => i.ToDto()).ToList());
    }

    public static KitchenTicketItemDto ToDto(this KitchenTicketItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new KitchenTicketItemDto(
            Id: item.Id.Value,
            OrderItemId: item.OrderItemId,
            MenuItemId: item.MenuItemId,
            MenuItemName: item.MenuItemName,
            Quantity: item.Quantity,
            UnitPrice: item.UnitPrice,
            SelectedVariations: item.SelectedVariations,
            Customizations: item.Customizations,
            SpecialInstructions: item.SpecialInstructions,
            SeatNumber: item.SeatNumber,
            Status: item.Status,
            StartedAt: item.StartedAt,
            ReadyAt: item.ReadyAt,
            StationId: item.StationId);
    }

    public static KitchenStationDto ToDto(this KitchenStation station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new KitchenStationDto(
            Id: station.Id.Value,
            RestaurantId: station.RestaurantId,
            Name: station.Name,
            SortOrder: station.SortOrder,
            IsActive: station.IsActive);
    }

    /// <summary>
    /// Maps an inbound <c>OrderCreatedIntegrationEvent</c> to the per-item
    /// seeds used by <c>KitchenTicket.CreateFromOrder</c>. Stays free of any
    /// payment-derived fields (the integration event carries none)
    /// </summary>
    public static IReadOnlyList<OrderItemSeed> ToOrderItemSeeds(this OrderCreatedIntegrationEvent evt) =>
        evt.Items
            .Select(i => new OrderItemSeed(
                OrderItemId: i.OrderItemId,
                MenuItemId: i.MenuItemId,
                MenuItemName: i.MenuItemName,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPrice,
                SelectedVariations: i.SelectedVariations,
                Customizations: i.Customizations,
                SpecialInstructions: i.SpecialInstructions,
                SeatNumber: i.SeatNumber))
            .ToList();
}