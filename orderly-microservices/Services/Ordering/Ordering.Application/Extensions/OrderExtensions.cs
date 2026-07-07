using System.Text.Json;
using BuildingBlocks.Messaging.Events;

namespace Ordering.Application.Extensions;

public static class OrderExtensions
{
    public static IEnumerable<OrderDto> ToOrderDtoList(this IEnumerable<Order> orders)
    {
        return orders.Select(o => o.ToOrderDto());
    }

    public static OrderDto ToOrderDto(this Order order)
    {
        return new OrderDto(
            Id: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            OrderNumber: order.OrderNumber.Value,
            RestaurantId: order.RestaurantId,
            Currency: order.Currency,
            Subtotal: order.Subtotal,
            TaxRate: order.TaxRate,
            TaxAmount: order.TaxAmount,
            DiscountAmount: order.DiscountAmount,
            DiscountCode: order.DiscountCode,
            TotalAmount: order.TotalAmount,
            Status: order.Status,
            OrderType: order.OrderType,
            BillingAddress: new AddressDto(order.BillingAddress.Street, order.BillingAddress.City, order.BillingAddress.State, order.BillingAddress.ZipCode, order.BillingAddress.Country),
            DeliveryAddress: new AddressDto(order.DeliveryAddress.Street, order.DeliveryAddress.City, order.DeliveryAddress.State, order.DeliveryAddress.ZipCode, order.DeliveryAddress.Country),
            DeliveryNotes: order.DeliveryNotes,
            DeliveryStatus: order.DeliveryStatus,
            DeliveryLatitude: order.DeliveryLatitude,
            DeliveryLongitude: order.DeliveryLongitude,
            Payment: new PaymentDto(order.Payment.CardName, order.Payment.CardNumber, order.Payment.Expiration, order.Payment.Ccv, order.Payment.PaymentMethod),
            EstimatedPrepTimeMinutes: order.EstimatedPrepTimeMinutes,
            ActualPrepTimeMinutes: order.ActualPrepTimeMinutes,
            IsModified: order.IsModified,
            RequiresAdminApproval: order.RequiresAdminApproval,
            TableId: order.TableId,
            CreatedByUserId: order.CreatedByUserId,
            ApprovedByAdminId: order.ApprovedByAdminId,
            ConfirmedByUserId: order.ConfirmedByUserId,
            CompletedByUserId: order.CompletedByUserId,
            ApprovedAt: order.ApprovedAt,
            CancelledAt: order.CancelledAt,
            CompletedAt: order.CompletedAt,
            ConfirmedAt: order.ConfirmedAt,
            DeliveredAt: order.DeliveredAt,
            PreparingStartedAt: order.PreparingStartedAt,
            ReadyAt: order.ReadyAt,
            Notes: order.Notes,
            OrderItems: [.. order.OrderItems.Select(oi => new OrderItemDto(
                Id: oi.Id.Value,
                OrderId: oi.OrderId.Value,
                MenuItemId: oi.MenuItemId.Value,
                MenuItemName: oi.MenuItemName,
                MenuItemDescription: oi.MenuItemDescription,
                MenuItemImageUrl: oi.MenuItemImageUrl,
                Quantity: oi.Quantity,
                UnitPrice: oi.UnitPrice,
                BasePrice: oi.BasePrice,
                TotalPrice: oi.TotalPrice,
                SeatNumber: oi.SeatNumber,
                SpecialInstructions: oi.SpecialInstructions,
                Customizations: oi.Customizations,
                SelectedVariations: oi.SelectedVariations,
                PrepStatus: oi.PrepStatus,
                CreatedAt: oi.CreatedAt,
                PrepStartedAt: oi.PrepStartedAt,
                PrepCompletedAt: oi.PrepCompletedAt
            ))]
        );
    }

    /// <summary>
    /// Maps the aggregate to the bus-safe <see cref="OrderCreatedIntegrationEvent"/>.
    /// Carries NO payment data — that field on <c>Order</c> is intentionally
    /// dropped here. See KITCHEN_INTEGRATION_PLAN.md Phase 1.
    /// </summary>
    public static OrderCreatedIntegrationEvent ToOrderCreatedIntegrationEvent(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderCreatedIntegrationEvent
        {
            OrderId = order.Id.Value,
            OrderNumber = order.OrderNumber.Value,
            RestaurantId = order.RestaurantId,
            TableId = order.TableId,
            OrderType = (int)order.OrderType,
            CustomerId = order.CustomerId.Value,
            Subtotal = order.Subtotal,
            TotalAmount = order.TotalAmount,
            TaxAmount = order.TaxAmount,
            DiscountAmount = order.DiscountAmount,
            Currency = order.Currency,
            DiscountCode = string.IsNullOrEmpty(order.DiscountCode) ? null : order.DiscountCode,
            BillingAddress = MapAddress(order.BillingAddress),
            DeliveryAddress = order.OrderType == OrderType.Delivery
                ? MapAddress(order.DeliveryAddress)
                : null,
            Items = order.OrderItems.Select(MapItem).ToList(),
            EstimatedPrepTimeMinutes = order.EstimatedPrepTimeMinutes,
            Notes = order.Notes ?? string.Empty,
        };
    }

    private static OrderAddress MapAddress(Address address) =>
        new(address.Street, address.City, address.State, address.ZipCode, address.Country);

    private static KitchenOrderItemPreview MapItem(OrderItem oi) =>
        new(
            OrderItemId: oi.Id.Value,
            MenuItemId: oi.MenuItemId.Value,
            MenuItemName: oi.MenuItemName,
            Quantity: oi.Quantity,
            UnitPrice: oi.UnitPrice,
            SelectedVariations: DeserializeVariations(oi.SelectedVariations),
            Customizations: DeserializeCustomizations(oi.Customizations),
            SpecialInstructions: string.IsNullOrEmpty(oi.SpecialInstructions) ? null : oi.SpecialInstructions,
            SeatNumber: oi.SeatNumber > 0 ? oi.SeatNumber : null);

    /// <summary>
    /// Phase D: deserialize the jsonb <c>SelectedVariations</c> column into
    /// <see cref="KitchenOrderItemVariation"/> records. Tolerates the legacy
    /// <c>string[]</c> shape (each entry becomes a <c>(Name, 0)</c>
    /// record) and the richer <c>{ Name, Price }</c> shape. Unparseable
    /// entries are logged and dropped — the kitchen display must never
    /// crash because a legacy column shape slipped through.
    /// </summary>
    private static IReadOnlyList<KitchenOrderItemVariation> DeserializeVariations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<KitchenOrderItemVariation>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<KitchenOrderItemVariation>();
            }

            var result = new List<KitchenOrderItemVariation>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        var name = element.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(new KitchenOrderItemVariation(name, 0m));
                        }
                        break;
                    case JsonValueKind.Object:
                        var variantName = element.TryGetProperty("Name", out var n)
                            ? n.GetString()
                            : element.TryGetProperty("name", out n) ? n.GetString() : null;
                        var price = 0m;
                        if (element.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number)
                        {
                            price = p.GetDecimal();
                        }
                        else if (element.TryGetProperty("price", out p) && p.ValueKind == JsonValueKind.Number)
                        {
                            price = p.GetDecimal();
                        }

                        if (!string.IsNullOrWhiteSpace(variantName))
                        {
                            result.Add(new KitchenOrderItemVariation(variantName!, price));
                        }
                        break;
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<KitchenOrderItemVariation>();
        }
    }

    /// <summary>
    /// Deserialize the jsonb <c>Customizations</c> column into
    /// <see cref="KitchenOrderItemCustomization"/> records. Tolerates the
    /// legacy <c>string[]</c> shape (each entry becomes a
    /// <c>(Label=entry, Value=null, Price=null)</c> record) and the richer
    /// <c>{ Label, Value, Price }</c> shape. Unparseable entries are
    /// dropped silently so a single malformed row doesn't sink the bus.
    /// </summary>
    private static IReadOnlyList<KitchenOrderItemCustomization> DeserializeCustomizations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<KitchenOrderItemCustomization>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<KitchenOrderItemCustomization>();
            }

            var result = new List<KitchenOrderItemCustomization>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        var label = element.GetString();
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            result.Add(new KitchenOrderItemCustomization(label!, null, null));
                        }
                        break;
                    case JsonValueKind.Object:
                        var custLabel = TryGetString(element, "Label", "label");
                        if (string.IsNullOrWhiteSpace(custLabel))
                        {
                            break;
                        }
                        var value = TryGetString(element, "Value", "value");
                        decimal? price = null;
                        if (TryGetDecimal(element, "Price", "price") is { } p)
                        {
                            price = p;
                        }
                        result.Add(new KitchenOrderItemCustomization(custLabel!, value, price));
                        break;
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<KitchenOrderItemCustomization>();
        }
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static decimal? TryGetDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
                && prop.TryGetDecimal(out var d))
            {
                return d;
            }
        }
        return null;
    }
}
