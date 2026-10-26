namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreatedEventHandler(IOutboxPublisher outboxPublisher, ILogger<OrderCreatedEventHandler> logger)
    : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Domain Event handled: {DomainEvent}", domainEvent.GetType().Name);

        // Publish the bus-safe contract — never the internal OrderDto.
        // Payment data MUST NOT cross this boundary. See KITCHEN_INTEGRATION_PLAN.md Phase 1.
        OrderCreatedIntegrationEvent integrationEvent = domainEvent.Order.ToOrderCreatedIntegrationEvent();

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
    }
}
