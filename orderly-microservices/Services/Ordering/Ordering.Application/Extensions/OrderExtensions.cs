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
}
