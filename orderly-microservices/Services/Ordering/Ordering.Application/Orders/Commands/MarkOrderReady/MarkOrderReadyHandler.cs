namespace Ordering.Application.Orders.Commands.MarkOrderReady;

public class MarkOrderReadyHandler(IApplicationDbContext dbContext)
    : ICommandHandler<MarkOrderReadyCommand, MarkOrderReadyResult>
{
    public async Task<MarkOrderReadyResult> Handle(
        MarkOrderReadyCommand command,
        CancellationToken cancellationToken)
    {
        OrderId? orderId = OrderId.Of(command.OrderId);

        Order? order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        order.MarkReady(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MarkOrderReadyResult(true);
    }
}