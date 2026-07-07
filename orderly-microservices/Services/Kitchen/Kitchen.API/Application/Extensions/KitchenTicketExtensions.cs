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
    /// payment-derived fields (the integration event carries none).
    ///
    /// Phase D: variations and customizations arrive as typed records;
    /// <see cref="FlattenVariations"/> and <see cref="FlattenCustomizations"/>
    /// collapse them back to the jsonb-compatible
    /// <c>IReadOnlyList&lt;string&gt;</c> shape the aggregate still uses.
    /// </summary>
    public static IReadOnlyList<OrderItemSeed> ToOrderItemSeeds(this OrderCreatedIntegrationEvent evt) =>
        evt.Items
            .Select(i => new OrderItemSeed(
                OrderItemId: i.OrderItemId,
                MenuItemId: i.MenuItemId,
                MenuItemName: i.MenuItemName,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPrice,
                SelectedVariations: FlattenVariations(i.SelectedVariations),
                Customizations: FlattenCustomizations(i.Customizations),
                SpecialInstructions: i.SpecialInstructions,
                SeatNumber: i.SeatNumber))
            .ToList();

    /// <summary>
    /// Renders a variation as "<c>Name</c>" or, when a non-zero price is
    /// present, "<c>Name (+$X.XX)</c>". Empty <c>Name</c>s are dropped.
    /// </summary>
    private static IReadOnlyList<string> FlattenVariations(
        IReadOnlyList<BuildingBlocks.Messaging.Events.KitchenOrderItemVariation> variations)
    {
        if (variations.Count == 0)
        {
            return [];
        }

        var result = new List<string>(variations.Count);
        foreach (var v in variations)
        {
            if (string.IsNullOrWhiteSpace(v.Name))
            {
                continue;
            }

            result.Add(v.Price == 0m
                ? v.Name
                : $"{v.Name} (+${v.Price:0.00})");
        }
        return result;
    }

    /// <summary>
    /// Renders a customization as "<c>Label</c>" or, when a value is
    /// present, "<c>Label: Value</c>". Price is omitted (it's already in
    /// the per-item total). Empty <c>Label</c>s are dropped.
    /// </summary>
    private static IReadOnlyList<string> FlattenCustomizations(
        IReadOnlyList<BuildingBlocks.Messaging.Events.KitchenOrderItemCustomization> customizations)
    {
        if (customizations.Count == 0)
        {
            return [];
        }

        var result = new List<string>(customizations.Count);
        foreach (var c in customizations)
        {
            if (string.IsNullOrWhiteSpace(c.Label))
            {
                continue;
            }

            result.Add(string.IsNullOrWhiteSpace(c.Value)
                ? c.Label
                : $"{c.Label}: {c.Value}");
        }
        return result;
    }
}