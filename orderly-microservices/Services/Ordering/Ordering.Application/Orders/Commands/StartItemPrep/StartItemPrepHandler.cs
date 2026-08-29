namespace Ordering.Application.Orders.Commands.StartItemPrep;

public class StartItemPrepHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartItemPrepCommand, StartItemPrepResult>
{
    public async Task<StartItemPrepResult> Handle(
        StartItemPrepCommand command,
        CancellationToken cancellationToken)
    {
        OrderItem? item = await OrderItemMutationHelpers.LoadOrderWithItemAsync(
            dbContext, command.OrderId, command.OrderItemId, cancellationToken);

        item.MarkItemPreparing(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartItemPrepResult(true);
    }
}