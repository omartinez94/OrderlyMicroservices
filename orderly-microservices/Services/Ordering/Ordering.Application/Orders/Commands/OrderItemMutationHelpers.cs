namespace Ordering.Application.Orders.Commands;

/// <summary>
/// Shared helper for handlers that mutate a single <see cref="OrderItem"/>
/// on an <see cref="Order"/> aggregate. Both <c>MarkItemReadyHandler</c>
/// and <c>StartItemPrepHandler</c> load the order with its items, wire
/// the CLR-only <see cref="OrderItem.Parent"/> back-reference, and look
/// up the targeted item by id — this consolidates those three steps.
/// </summary>
/// <remarks>
/// <para><b>Why Include then wire:</b> <c>FindAsync</c> does not eager-load
/// navigations, so the items collection comes back empty. Switching to
/// <c>Include(o =&gt; o.OrderItems)</c> populates the collection but
/// leaves the CLR-only <see cref="OrderItem.Parent"/> back-reference
/// null on every loaded item — without the explicit wiring
/// <see cref="OrderItem.MarkItemPreparing"/> / <see cref="OrderItem.MarkItemReady"/>
/// NRE on <c>Parent.RecordActivity(...)</c>.</para>
/// <para><b>Preserve the order:</b> <c>Include</c> → <c>Parent = order</c>
/// → <c>SingleOrDefault</c> is load-bearing. Don't reorder.</para>
/// </remarks>
internal static class OrderItemMutationHelpers
{
    public static async Task<OrderItem> LoadOrderWithItemAsync(
        IApplicationDbContext dbContext,
        Guid orderIdRaw,
        Guid orderItemIdRaw,
        CancellationToken cancellationToken)
    {
        OrderId? orderId = OrderId.Of(orderIdRaw);

        Order? order = await dbContext.Orders
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), orderIdRaw);

        foreach (OrderItem? loadedItem in order.OrderItems)
        {
            loadedItem.Parent = order;
        }

        OrderItemId? itemId = OrderItemId.Of(orderItemIdRaw);
        return order.OrderItems.SingleOrDefault(oi => oi.Id == itemId)
            ?? throw new OrderItemNotFoundException(nameof(OrderItem), orderItemIdRaw);
    }
}
