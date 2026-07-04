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
            SelectedVariations: DeserializeStringList(oi.SelectedVariations),
            Customizations: DeserializeStringList(oi.Customizations),
            SpecialInstructions: string.IsNullOrEmpty(oi.SpecialInstructions) ? null : oi.SpecialInstructions,
            SeatNumber: oi.SeatNumber > 0 ? oi.SeatNumber : null);

    /// <summary>
    /// <c>OrderItem.SelectedVariations</c> / <c>Customizations</c> are stored as
    /// jsonb on the aggregate. The integration event declares them as
    /// <c>IReadOnlyList&lt;string&gt;</c> per KITCHEN_SERVICE_PLAN.md §10.1; the
    /// source JSON may carry richer objects (Basket uses
    /// <c>{ Ingredient, Action }</c>), so this is a best-effort parse. Empty
    /// string and non-array JSON both yield an empty list — downstream consumers
    /// can rely on never receiving <c>null</c>.
    /// </summary>
    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
