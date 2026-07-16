namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderCancelledIntegrationEvent"/> and
/// cancels the upstream <c>Order</c> via <see cref="Order.Cancel"/>. The
/// aggregate's legal-transition guards apply — a kitchen-cancellation
/// arriving after the order has already been completed or delivered is
/// surfaced as <see cref="Ordering.Domain.Exceptions.InvalidOrderStateTransitionException"/>
/// and the message is nacked for broker retry.
/// </summary>
public class KitchenOrderCancelledIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderCancelledIntegrationEventHandler> logger)
    : IConsumer<KitchenOrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderCancelledIntegrationEvent> context)
    {
        var message = context.Message;
        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderCancelledIntegrationEvent),
                message.OrderId,
                correlationId);

            var order = await dbContext.Orders.FindAsync(
                [OrderId.Of(message.OrderId)], context.CancellationToken);

            if (order is null)
            {
                logger.LogWarning(
                    "Skipping {Event} — Order {OrderId} not found.",
                    nameof(KitchenOrderCancelledIntegrationEvent),
                    message.OrderId);
                return;
            }

            order.Cancel(message.Reason, message.CancelledByUserId, message.CancelledAt);

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}