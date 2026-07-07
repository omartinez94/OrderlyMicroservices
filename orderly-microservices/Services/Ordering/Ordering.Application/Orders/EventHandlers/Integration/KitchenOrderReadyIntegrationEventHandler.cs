namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderReadyIntegrationEvent"/> and drives
/// the upstream <c>Order</c> from <c>Preparing</c> to <c>Ready</c> via
/// <see cref="Order.MarkReady"/>. Aggregate guards apply — illegal
/// transitions are logged and the message is nacked for broker retry.
/// </summary>
public class KitchenOrderReadyIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderReadyIntegrationEventHandler> logger)
    : IConsumer<KitchenOrderReadyIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderReadyIntegrationEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId}",
            nameof(KitchenOrderReadyIntegrationEvent),
            message.OrderId);

        var order = await dbContext.Orders.FindAsync(
            [OrderId.Of(message.OrderId)], context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning(
                "Skipping {Event} — Order {OrderId} not found.",
                nameof(KitchenOrderReadyIntegrationEvent),
                message.OrderId);
            return;
        }

        order.MarkReady(message.ReadyAt);

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}