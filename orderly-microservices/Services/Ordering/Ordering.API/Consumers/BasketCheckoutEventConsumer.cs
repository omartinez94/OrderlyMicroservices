using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.API.Consumers;

public class BasketCheckoutEventConsumer(ISender sender) : IConsumer<BasketCheckoutEvent>
{
    public async Task Consume(ConsumeContext<BasketCheckoutEvent> context)
    {
        var message = context.Message;

        var orderItems = message.Items.Select(item => new OrderItemDto(
            Id: Guid.Empty,
            OrderId: Guid.Empty,
            MenuItemId: Guid.TryParse(item.MenuItemId.ToString(), out var menuItemGuid) ? menuItemGuid : Guid.Empty,
            MenuItemName: string.Empty,
            MenuItemDescription: string.Empty,
            MenuItemImageUrl: string.Empty,
            Quantity: item.Quantity,
            UnitPrice: item.UnitPrice,
            BasePrice: item.UnitPrice,
            TotalPrice: item.TotalPrice,
            SeatNumber: 0,
            SpecialInstructions: string.Empty,
            Customizations: string.Empty,
            SelectedVariations: string.Empty,
            PrepStatus: PrepStatus.Pending,
            CreatedAt: Instant.FromDateTimeOffset(DateTimeOffset.UtcNow),
            PrepStartedAt: null,
            PrepCompletedAt: null
        )).ToList();

        var orderDto = new OrderDto(
            Id: Guid.Empty,
            CustomerId: message.UserId,
            OrderNumber: Guid.NewGuid().ToString()[..8].ToUpperInvariant(),
            RestaurantId: message.RestaurantId,
            Currency: "USD",
            Subtotal: message.TotalAmount,
            TaxRate: 0,
            TaxAmount: 0,
            DiscountAmount: 0,
            DiscountCode: message.AppliedDiscounts.FirstOrDefault() ?? string.Empty,
            TotalAmount: message.TotalAmount,
            Status: OrderStatus.Pending,
            OrderType: OrderType.Delivery,
            BillingAddress: new AddressDto("Default Street", "Default City", "Default State", "00000", "USA"),
            DeliveryAddress: new AddressDto("Default Street", "Default City", "Default State", "00000", "USA"),
            DeliveryNotes: string.Empty,
            DeliveryStatus: null,
            DeliveryLatitude: null,
            DeliveryLongitude: null,
            Payment: new PaymentDto("N/A", "0000000000000000", "12/25", "000", "Cash"),
            EstimatedPrepTimeMinutes: 30,
            ActualPrepTimeMinutes: 0,
            IsModified: false,
            RequiresAdminApproval: false,
            TableId: null,
            CreatedByUserId: message.UserId,
            ApprovedByAdminId: null,
            ConfirmedByUserId: null,
            CompletedByUserId: null,
            ApprovedAt: null,
            CancelledAt: null,
            CompletedAt: null,
            ConfirmedAt: null,
            DeliveredAt: null,
            PreparingStartedAt: null,
            ReadyAt: null,
            Notes: "Created from basket checkout",
            OrderItems: orderItems
        );

        var command = new CreateOrderCommand(orderDto);

        await sender.Send(command, context.CancellationToken);
    }
}