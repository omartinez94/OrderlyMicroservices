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
    ILogger<KitchenOrderCancelledIntegrationEventHandler> logger,
    ICurrentRestaurantProvider? tenantProvider = null)
    : IConsumer<KitchenOrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<KitchenOrderCancelledIntegrationEvent> context)
    {
        KitchenOrderCancelledIntegrationEvent? message = context.Message;
        string? correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        CorrelationContext.Set(correlationId);

        try
        {
            logger.LogInformation(
                "Integration Event handled: {IntegrationEvent} for Order {OrderId} (CorrelationId: {CorrelationId})",
                nameof(KitchenOrderCancelledIntegrationEvent),
                message.OrderId,
                correlationId);

            using IDisposable? tenantScope = message.RestaurantId != Guid.Empty
                ? tenantProvider?.Attach(
                    new ClaimsPrincipalBuilder()
                        .WithRestaurant(message.RestaurantId)
                        .WithUser(message.CancelledByUserId)
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
