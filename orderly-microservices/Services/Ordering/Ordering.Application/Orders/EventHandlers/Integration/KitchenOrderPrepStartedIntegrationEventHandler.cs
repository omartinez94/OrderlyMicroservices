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
    ILogger<KitchenOrderPrepStartedIntegrationEventHandler> logger)
    : IConsumer<KitchenOrderPrepStartedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderPrepStartedIntegrationEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId} (item {ItemId})",
            nameof(KitchenOrderPrepStartedIntegrationEvent),
            message.OrderId,
            message.ItemId);

        var order = await dbContext.Orders.FindAsync(
            [OrderId.Of(message.OrderId)], context.CancellationToken);

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
}