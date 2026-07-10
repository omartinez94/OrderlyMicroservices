namespace Ordering.Application.Orders.Commands.StartOrderPrep;

public class StartOrderPrepHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartOrderPrepCommand, StartOrderPrepResult>
{
    public async Task<StartOrderPrepResult> Handle(
        StartOrderPrepCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartOrderPrepResult(true);
    }
}