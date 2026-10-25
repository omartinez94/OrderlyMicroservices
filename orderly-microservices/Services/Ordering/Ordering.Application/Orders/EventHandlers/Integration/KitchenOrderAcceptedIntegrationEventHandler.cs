namespace Ordering.Application.Orders.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="KitchenOrderAcceptedIntegrationEvent"/> and drives
/// the upstream <c>Order</c> from <c>Pending</c> to <c>Confirmed</c> via
/// <see cref="Order.Confirm"/>. The aggregate's legal-transition guards
/// apply, so a duplicate or out-of-order delivery results in
/// <see cref="Ordering.Domain.Exceptions.InvalidOrderStateTransitionException"/>.
/// In that case the message is nacked (MassTransit transient fault →
/// broker retry); once <see cref="MassTransit.IConsumer{T}.Consume"/>
/// returns successfully the event id is recorded as processed
/// </summary>
public class KitchenOrderAcceptedIntegrationEventHandler(
    IApplicationDbContext dbContext,
    ILogger<KitchenOrderAcceptedIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? tenantProvider = null)
    : IConsumer<KitchenOrderAcceptedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderAcceptedIntegrationEvent> context)
    {
        KitchenOrderAcceptedIntegrationEvent? message = context.Message;
        string? correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderAcceptedIntegrationEvent),
                message.OrderId,
                correlationId);

            using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
                ? tenantProvider?.Attach(
                    new ClaimsPrincipalBuilder()
                        .WithRestaurant(message.RestaurantId)
                        .WithUser(message.ConfirmedByUserId)
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
                    nameof(KitchenOrderAcceptedIntegrationEvent),
                    message.OrderId);
                return;
            }

            order.Confirm(message.ConfirmedByUserId, message.ConfirmedAt);

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
