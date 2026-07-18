using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.Application.Orders.EventHandlers.Integration;

public class BasketCheckoutEventHandler(ISender sender, ILogger<BasketCheckoutEventHandler> logger) : IConsumer<BasketCheckoutEvent>
{
    public async Task Consume(ConsumeContext<BasketCheckoutEvent> context)
    {
        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} (CorrelationId: {CorrelationId})",
                context.Message.GetType().Name,
                correlationId);

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
                Customizations: [],
                SelectedVariations: [],
                PrepStatus: PrepStatus.Pending,
                CreatedAt: Instant.FromDateTimeOffset(DateTimeOffset.UtcNow),
                PrepStartedAt: null,
                PrepCompletedAt: null
            )).ToList();

            var address = new AddressDto(message.AddressLine, message.City, message.State, message.ZipCode, message.Country);

            // BasketCheckoutEvent v2 publishes only
            // PaymentMethodSummary (discriminator + brand + last-four).
            // Full PAN and CVV never leave Basket's process boundary.
            // Defensive fallback for the v1→v2 transition window: if a
            // pre-v2 row slips through (outbox row stamped before the
            // upgrade), the summary is null — fall back to Unspecified +
            // "Unknown" / "0000" so the consumer doesn't crash on null.
            var summary = message.PaymentMethodSummary;
            var payment = summary is null
                ? new PaymentDto(
                    Method: PaymentMethod.Unspecified,
                    Brand: "Unknown",
                    LastFour: "0000")
                : new PaymentDto(
                    Method: summary.Method,
                    Brand: summary.Brand,
                    LastFour: summary.LastFour);

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
                BillingAddress: address,
                DeliveryAddress: address,
                DeliveryNotes: string.Empty,
                DeliveryStatus: null,
                DeliveryLatitude: null,
                DeliveryLongitude: null,
                Payment: payment,
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
                OrderItems: orderItems,
                Activities: []
            );

            var command = new CreateOrderCommand(orderDto);

            await sender.Send(command, context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
