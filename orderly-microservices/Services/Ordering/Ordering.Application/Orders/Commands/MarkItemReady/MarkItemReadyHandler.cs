namespace Ordering.Application.Orders.Commands.MarkItemReady;

public class MarkItemReadyHandler(IApplicationDbContext dbContext)
    : ICommandHandler<MarkItemReadyCommand, MarkItemReadyResult>
{
    public async Task<MarkItemReadyResult> Handle(
        MarkItemReadyCommand command,
        CancellationToken cancellationToken)
    {
        OrderItem? item = await OrderItemMutationHelpers.LoadOrderWithItemAsync(
            dbContext, command.OrderId, command.OrderItemId, cancellationToken);

        item.MarkItemReady(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MarkItemReadyResult(true);
    }
}