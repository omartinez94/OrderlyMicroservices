namespace Ordering.Application.Orders.Commands.CreateOrder;

public class CreateOrderHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var dto = command.Order;

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

        var orderId = OrderId.Of(Guid.NewGuid());

        var order = Order.Create(
            orderId,
            CustomerId.Of(dto.CustomerId),
            OrderNumber.Of(dto.OrderNumber),
            dto.RestaurantId,
            billingAddress,
            deliveryAddress,
            payment);

        // Scalar fields not covered by Order.Create
        order.Currency              = dto.Currency;
        order.OrderType             = dto.OrderType;
        order.Notes                 = dto.Notes;
        order.DeliveryNotes         = dto.DeliveryNotes;
        order.EstimatedPrepTimeMinutes = dto.EstimatedPrepTimeMinutes;
        order.RequiresAdminApproval = dto.RequiresAdminApproval;
        order.TableId               = dto.TableId;
        order.CreatedByUserId       = dto.CreatedByUserId;

        // Add order items
        foreach (var item in dto.OrderItems)
        {
            order.Add(MenuItemId.Of(item.MenuItemId), item.Quantity, item.UnitPrice);
        }

        // Append the OrderCreated activity row. Order.Create cannot call
        // RecordActivity itself (the aggregate isn't in scope of the
        // factory); this is the application-side entry point that does.
        order.RecordActivity(
            Ordering.Domain.Enums.OrderActivityType.OrderCreated,
            actorUserId: dto.CreatedByUserId,
            occurredAt: SystemClock.Instance.GetCurrentInstant());

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOrderResult(orderId.Value);
    }
}
