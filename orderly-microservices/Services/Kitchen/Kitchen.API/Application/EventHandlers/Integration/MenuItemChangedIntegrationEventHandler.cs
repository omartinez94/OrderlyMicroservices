using BuildingBlocks.Messaging.Events.Catalog;
using Kitchen.API.Hubs;

namespace Kitchen.API.Application.EventHandlers.Integration;

/// <summary>
/// Consumes <see cref="MenuItemChangedIntegrationEvent"/> from Catalog.API and relays
/// the change to the connected KitchenHub clients in group <c>restaurant:{restaurantId}</c>.
/// Broadcast-only; completely idempotent with zero local persistence.
/// </summary>
public class MenuItemChangedIntegrationEventHandler(
    IHubContext<KitchenHub, IKitchenHubClient> hubContext,
    ILogger<MenuItemChangedIntegrationEventHandler> logger) : IConsumer<MenuItemChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MenuItemChangedIntegrationEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MenuItemChangedIntegrationEvent message = context.Message;

        logger.LogInformation(
            "Consuming MenuItemChangedIntegrationEvent for Item {ItemId}, Restaurant {RestaurantId}, ChangeType {ChangeType}",
            message.MenuItemId,
            message.RestaurantId,
            message.ChangeType);

        MenuItemChangedDto dto = new(
            MenuItemId: message.MenuItemId,
            RestaurantId: message.RestaurantId,
            ChangeType: message.ChangeType.ToString(),
            Name: message.Name,
            BasePrice: message.BasePrice,
            IsAvailable: message.IsAvailable,
            AvailabilityStatus: message.AvailabilityStatus,
            ImageUrl: message.ImageUrl,
            DefaultCourseNumber: message.DefaultCourseNumber,
            OccurredAt: message.OccurredOn);

        await hubContext.Clients
            .Group($"restaurant:{message.RestaurantId}")
            .MenuItemChanged(dto)
            .ConfigureAwait(false);
    }
}
