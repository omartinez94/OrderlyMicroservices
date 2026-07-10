namespace Ordering.Application.Orders.Commands.StartItemPrep;

public class StartItemPrepHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartItemPrepCommand, StartItemPrepResult>
{
    public async Task<StartItemPrepResult> Handle(
        StartItemPrepCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        var itemId = OrderItemId.Of(command.OrderItemId);
        var item = order.OrderItems.SingleOrDefault(oi => oi.Id == itemId)
            ?? throw new OrderItemNotFoundException(nameof(OrderItem), command.OrderItemId);

        item.MarkItemPreparing(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartItemPrepResult(true);
    }
}