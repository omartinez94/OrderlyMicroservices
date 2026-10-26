namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderCancelRejectedIntegrationEvent"/> and
/// restores the upstream <c>Order</c> from <c>CancelRequested</c> back to its previous
/// status via <see cref="Order.RejectCancel(Instant, string?, string?)"/>.
/// </summary>
public class KitchenOrderCancelRejectedIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderCancelRejectedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? currentRestaurantProvider = null)
    : IConsumer<KitchenOrderCancelRejectedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderCancelRejectedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        KitchenOrderCancelRejectedIntegrationEvent message = context.Message;
        string correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderCancelRejectedIntegrationEvent),
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
                    nameof(KitchenOrderCancelRejectedIntegrationEvent),
                    message.OrderId);
                return;
            }

            if (order.Status != OrderStatus.CancelRequested)
            {
                logger.LogInformation(
                    "Skipping {Event} — Order {OrderId} is not in CancelRequested state (CurrentStatus: {Status}).",
                    nameof(KitchenOrderCancelRejectedIntegrationEvent),
                    message.OrderId,
                    order.Status);
                return;
            }

            order.RejectCancel(message.RejectedAt, message.RejectedByUserId, message.Reason);

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation(
                "Restored Order {OrderId} from CancelRequested back to {Status} after kitchen rejected cancel.",
                order.Id.Value,
                order.Status);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
