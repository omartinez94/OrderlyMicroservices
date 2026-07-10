namespace Ordering.Application.Orders.Commands.MarkOrderReady;

public class MarkOrderReadyHandler(IApplicationDbContext dbContext)
    : ICommandHandler<MarkOrderReadyCommand, MarkOrderReadyResult>
{
    public async Task<MarkOrderReadyResult> Handle(
        MarkOrderReadyCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        order.MarkReady(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MarkOrderReadyResult(true);
    }
}