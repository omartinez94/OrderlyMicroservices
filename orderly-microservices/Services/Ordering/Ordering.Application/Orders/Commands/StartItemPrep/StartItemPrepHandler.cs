namespace Ordering.Application.Orders.Commands.StartItemPrep;

public class StartItemPrepHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartItemPrepCommand, StartItemPrepResult>
{
    public async Task<StartItemPrepResult> Handle(
        StartItemPrepCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        // Load the order with OrderItems eagerly — the handler mutates
        // a single item by id, so the items collection must be on the
        // aggregate before the lookup. FindAsync does not eager-load
        // navigations; the previous implementation surfaced 404
        // OrderItemNotFoundException because findAsync returned a
        // tracked entity whose OrderItems collection was empty.
        var order = await dbContext.Orders
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        // Restore the OrderItem → Order back-reference. EF Core's
        // Include loads the OrderItems collection but does NOT wire up
        // the CLR-only `Parent` property (it's an internal back-ref,
        // not a mapped navigation). OrderItem.MarkItemPreparing calls
        // Parent.RecordActivity(...) — without this wiring the call
        // NREs. Order.Add sets Parent in the write path; the read
        // path needs to do it explicitly.
        foreach (var loadedItem in order.OrderItems)
        {
            loadedItem.Parent = order;
        }

        var itemId = OrderItemId.Of(command.OrderItemId);
        var item = order.OrderItems.SingleOrDefault(oi => oi.Id == itemId)
            ?? throw new OrderItemNotFoundException(nameof(OrderItem), command.OrderItemId);

        item.MarkItemPreparing(SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartItemPrepResult(true);
    }
}