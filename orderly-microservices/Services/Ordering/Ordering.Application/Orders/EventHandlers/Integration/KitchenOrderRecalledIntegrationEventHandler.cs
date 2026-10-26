namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderRecalledIntegrationEvent"/> and reverts
/// the upstream <c>Order</c> from <c>Delivered</c> back to <c>Ready</c> via
/// <see cref="Order.RecallDelivery"/>.
/// </summary>
public class KitchenOrderRecalledIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderRecalledIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<KitchenOrderRecalledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderRecalledIntegrationEvent> context)
    {
        KitchenOrderRecalledIntegrationEvent? message = context.Message;
        string? correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderRecalledIntegrationEvent),
                message.OrderId,
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
                    nameof(KitchenOrderRecalledIntegrationEvent),
                    message.OrderId);
                return;
            }

            order.RecallDelivery(message.RecalledAt);

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
