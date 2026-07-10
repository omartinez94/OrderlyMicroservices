namespace Ordering.Application.Orders.Commands.MarkOrderDelivered;

public class MarkOrderDeliveredHandler(IApplicationDbContext dbContext)
    : ICommandHandler<MarkOrderDeliveredCommand, MarkOrderDeliveredResult>
{
    public async Task<MarkOrderDeliveredResult> Handle(
        MarkOrderDeliveredCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        order.MarkDelivered(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MarkOrderDeliveredResult(true);
    }
}