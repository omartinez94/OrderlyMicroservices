using Microsoft.FeatureManagement;

namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreatedEventHandler(IPublishEndpoint publishEndpoint, IFeatureManager featureManager, ILogger<OrderCreatedEventHandler> logger) 
    : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Domain Event handled: {DomainEvent}", domainEvent.GetType().Name);

        bool orderFullfilmentIsEnabled = await featureManager.IsEnabledAsync("OrderFullfilment");

        if (!orderFullfilmentIsEnabled)
        {
            return;
        }

        OrderDto orderCreatedIntegrationEvent = domainEvent.Order.ToOrderDto();

        await publishEndpoint.Publish(orderCreatedIntegrationEvent, cancellationToken);
    }
}
