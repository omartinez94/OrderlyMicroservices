namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderPrepStartedIntegrationEvent"/> and drives
/// the upstream <c>Order</c> from <c>Confirmed</c> to <c>Preparing</c> via
/// <see cref="Order.MarkPreparing"/>. Aggregate guards apply — illegal
/// transitions are logged and the message is nacked for broker retry. This is
/// the production path that supersedes the manual
/// <c>POST /orders/{id}/start-prep</c> override (which is kept as a manual
/// fallback).
/// </summary>
public class KitchenOrderPrepStartedIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderPrepStartedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? tenantProvider = null)
    : IConsumer<KitchenOrderPrepStartedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderPrepStartedIntegrationEvent> context)
    {
        KitchenOrderPrepStartedIntegrationEvent? message = context.Message;
        string? correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (item {ItemId}, CorrelationId: {CorrelationId})",
                nameof(KitchenOrderPrepStartedIntegrationEvent),
                message.OrderId,
                message.ItemId,
                correlationId);

            using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
                ? tenantProvider?.Attach(
                    new ClaimsPrincipalBuilder()
                        .WithRestaurant(message.RestaurantId)
                        .WithUser(message.StaffUserId)
                        .WithActor("ordering-kitchen-consumer")
                        .Build())
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
                    nameof(KitchenOrderPrepStartedIntegrationEvent),
                    message.OrderId);
                return;
            }

            order.MarkPreparing(message.StartedAt);

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
