namespace Ordering.Application.Orders.EventHandlers.Integration;

using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Messaging.Events.Ordering;
using MassTransit;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Interfaces;

public class DashboardInvalidationConsumer(
    IDashboardSnapshotCache cache,
    ILogger<DashboardInvalidationConsumer> logger)
    : IConsumer<OrderCreatedIntegrationEvent>,
      IConsumer<OrderCompletedIntegrationEvent>,
      IConsumer<OrderCancelledIntegrationEvent>,
      IConsumer<KitchenOrderAcceptedIntegrationEvent>,
      IConsumer<KitchenOrderPrepStartedIntegrationEvent>,
      IConsumer<KitchenOrderReadyIntegrationEvent>,
      IConsumer<KitchenOrderBumpedIntegrationEvent>,
      IConsumer<KitchenOrderCancelledIntegrationEvent>,
      IConsumer<KitchenOrderRecalledIntegrationEvent>,
      IConsumer<TableStatusChangedIntegrationEvent>,
      IConsumer<RestaurantConfigurationChangedIntegrationEvent>,
      IConsumer<MenuItemChangedIntegrationEvent>
{
    private static readonly IReadOnlyList<string> OrderSections = ["salesPace", "liveTicker", "openChecks"];
    private static readonly IReadOnlyList<string> KitchenSections = ["kitchenVelocity", "liveTicker", "alerts"];
    private static readonly IReadOnlyList<string> TableSections = ["floorPulse", "alerts"];
    private static readonly IReadOnlyList<string> ConfigSections = ["meta", "salesPace", "floorPulse"];
    private static readonly IReadOnlyList<string> MenuSections = ["alerts"];

    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, OrderSections, nameof(OrderCreatedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderCompletedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, OrderSections, nameof(OrderCompletedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderCancelledIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, OrderSections, nameof(OrderCancelledIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderAcceptedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderAcceptedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderPrepStartedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderPrepStartedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderReadyIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderReadyIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderBumpedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderBumpedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderCancelledIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderCancelledIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<KitchenOrderRecalledIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, KitchenSections, nameof(KitchenOrderRecalledIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<TableStatusChangedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, TableSections, nameof(TableStatusChangedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<RestaurantConfigurationChangedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, ConfigSections, nameof(RestaurantConfigurationChangedIntegrationEvent), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<MenuItemChangedIntegrationEvent> context)
    {
        await InvalidateAsync(context.Message.RestaurantId, MenuSections, nameof(MenuItemChangedIntegrationEvent), context.CancellationToken);
    }

    private async Task InvalidateAsync(
        Guid restaurantId,
        IReadOnlyList<string> sections,
        string eventName,
        CancellationToken cancellationToken)
    {
        if (restaurantId == Guid.Empty)
        {
            logger.LogDebug("Skipping dashboard cache invalidation for empty restaurant id on event {EventName}", eventName);
            return;
        }

        logger.LogDebug("Queueing dashboard invalidation for restaurant {RestaurantId} due to event {EventName}", restaurantId, eventName);
        await cache.InvalidateAsync(restaurantId, sections, cancellationToken);
    }
}
