namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderBumpedIntegrationEvent"/>. The kitchen
/// "bumped" transition (<c>Ready -&gt; Bumped</c>) does not change the
/// upstream <c>Order.Status</c> today — the order stays in <c>Ready</c>
/// until dispatched — but the event is recorded for audit and future
/// analytics consumers to attach. This handler exists so the broker
/// route is exercised end-to-end and the event is not silently dropped
/// on the Ordering side.
/// </summary>
public class KitchenOrderBumpedIntegrationEventHandler(
    ILogger<KitchenOrderBumpedIntegrationEventHandler> logger)
    : IConsumer<KitchenOrderBumpedIntegrationEvent>
{
    public Task Consume(ConsumeContext<KitchenOrderBumpedIntegrationEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "Integration Event handled: {IntegrationEvent} for Order {OrderId} by Staff {StaffId}",
            nameof(KitchenOrderBumpedIntegrationEvent),
            message.OrderId,
            message.BumpedByUserId);

        // No aggregate mutation today — bump is an audit signal only.
        // Reserved for a future per-state consumer (e.g. analytics).
        return Task.CompletedTask;
    }
}