namespace Ordering.Application.Orders.Commands.MarkItemReady;

public class MarkItemReadyHandler(IApplicationDbContext dbContext)
    : ICommandHandler<MarkItemReadyCommand, MarkItemReadyResult>
{
    public async Task<MarkItemReadyResult> Handle(
        MarkItemReadyCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        var itemId = OrderItemId.Of(command.OrderItemId);
        var item = order.OrderItems.SingleOrDefault(oi => oi.Id == itemId)
            ?? throw new OrderItemNotFoundException(nameof(OrderItem), command.OrderItemId);

        item.MarkItemReady(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MarkItemReadyResult(true);
    }
}