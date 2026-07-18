namespace Ordering.Application.Orders.Commands.UpdateOrder;

public class UpdateOrderHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateOrderCommand, UpdateOrderResult>
{
    public async Task<UpdateOrderResult> Handle(
        UpdateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var dto = command.Order;

        var order = await dbContext.Orders
            .FindAsync([OrderId.Of(dto.Id)], cancellationToken) ?? throw new OrderNotFoundException(nameof(Order), dto.Id);

        var billingAddress = Address.Of(
            dto.BillingAddress.Street,
            dto.BillingAddress.City,
            dto.BillingAddress.State,
            dto.BillingAddress.ZipCode,
            dto.BillingAddress.Country);

        var deliveryAddress = Address.Of(
            dto.DeliveryAddress.Street,
            dto.DeliveryAddress.City,
            dto.DeliveryAddress.State,
            dto.DeliveryAddress.ZipCode,
            dto.DeliveryAddress.Country);

        var payment = Payment.Of(
            dto.Payment.Method,
            dto.Payment.Brand,
            dto.Payment.LastFour);

        // Core update via domain method (raises OrderUpdatedEvent). Status
        // transitions are routed through the dedicated Confirm / MarkReady /
        // Cancel methods so the legal-transition guards apply.
        order.Update(billingAddress, deliveryAddress, payment);

        // Scalar fields not covered by Order.Update
        order.Currency                 = dto.Currency;
        order.Subtotal                 = dto.Subtotal;
        order.TaxRate                  = dto.TaxRate;
        order.TaxAmount                = dto.TaxAmount;
        order.DiscountAmount           = dto.DiscountAmount;
        order.DiscountCode             = dto.DiscountCode;
        order.TotalAmount              = dto.TotalAmount;
        order.OrderType                = dto.OrderType;
        order.Notes                    = dto.Notes;
        order.DeliveryNotes            = dto.DeliveryNotes;
        order.DeliveryStatus           = dto.DeliveryStatus;
        order.DeliveryLatitude         = dto.DeliveryLatitude;
        order.DeliveryLongitude        = dto.DeliveryLongitude;
        order.EstimatedPrepTimeMinutes = dto.EstimatedPrepTimeMinutes;
        order.ActualPrepTimeMinutes    = dto.ActualPrepTimeMinutes;
        order.IsModified               = dto.IsModified;
        order.RequiresAdminApproval    = dto.RequiresAdminApproval;
        order.TableId                  = dto.TableId;
        order.ApprovedByAdminId        = dto.ApprovedByAdminId;
        order.ConfirmedByUserId        = dto.ConfirmedByUserId;
        order.CompletedByUserId        = dto.CompletedByUserId;
        order.ApprovedAt               = dto.ApprovedAt;
        order.CancelledAt              = dto.CancelledAt;
        order.CompletedAt              = dto.CompletedAt;
        order.ConfirmedAt              = dto.ConfirmedAt;
        order.DeliveredAt              = dto.DeliveredAt;
        order.PreparingStartedAt       = dto.PreparingStartedAt;
        order.ReadyAt                  = dto.ReadyAt;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateOrderResult(true);
    }
}
