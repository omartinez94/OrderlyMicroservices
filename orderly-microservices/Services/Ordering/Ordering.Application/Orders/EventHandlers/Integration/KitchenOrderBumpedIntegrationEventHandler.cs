namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderBumpedIntegrationEvent"/> and drives
/// the upstream <c>Order</c> from <c>Ready</c> to <c>Delivered</c> via
/// <see cref="Order.MarkDelivered"/>.
/// </summary>
public class KitchenOrderBumpedIntegrationEventHandler(
    IApplicationDbContext dbContext,
    IClock clock,
    ILogger<KitchenOrderBumpedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<KitchenOrderBumpedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderBumpedIntegrationEvent> context)
    {
        KitchenOrderBumpedIntegrationEvent? message = context.Message;
        string? correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} by Staff {StaffId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderBumpedIntegrationEvent),
                message.OrderId,
                message.BumpedByUserId,
                correlationId);

            using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
                ? currentRestaurantProvider?.Attach(message.RestaurantId)
                : null;

            Order? order = await dbContext.Orders.FindAsync(
                [OrderId.Of(message.OrderId)], context.CancellationToken);

            if (order is null && message.RestaurantId == Guid.Empty && dbContext is DbContext)
            {
                order = await dbContext.Orders
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(o => o.Id == OrderId.Of(message.OrderId), context.CancellationToken);
            }

            if (order is null)
            {
                logger.LogWarning(
                    "Skipping {Event} — Order {OrderId} not found.",
                    nameof(KitchenOrderBumpedIntegrationEvent),
                    message.OrderId);
                return;
            }

            order.MarkDelivered(clock.GetCurrentInstant());

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
