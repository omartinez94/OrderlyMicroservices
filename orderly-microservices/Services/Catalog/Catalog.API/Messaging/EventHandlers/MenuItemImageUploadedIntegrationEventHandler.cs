using BuildingBlocks.Messaging.Events.Catalog;
using Catalog.API.Infrastructure.Storage;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Catalog.API.Messaging.EventHandlers;

/// <summary>
/// MassTransit <see cref="IConsumer{T}"/> for <see cref="MenuItemImageUploadedIntegrationEvent"/>.
/// Triggers asynchronous resizing and WebP thumbnail generation via <see cref="IImageProcessingService"/>.
/// </summary>
public sealed class MenuItemImageUploadedIntegrationEventHandler(
    IImageProcessingService imageProcessingService,
    ILogger<MenuItemImageUploadedIntegrationEventHandler> logger)
    : IConsumer<MenuItemImageUploadedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MenuItemImageUploadedIntegrationEvent> context)
    {
        MenuItemImageUploadedIntegrationEvent message = context.Message;

        logger.LogInformation(
            "Consuming MenuItemImageUploadedIntegrationEvent for MenuItem {MenuItemId} (Restaurant {RestaurantId})",
            message.MenuItemId, message.RestaurantId);

        ImageProcessingResult result = await imageProcessingService.ProcessMenuItemImageAsync(
            message.RestaurantId,
            message.MenuItemId,
            message.ObjectKey,
            context.CancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            logger.LogWarning(
                "Image processing returned failure for MenuItem {MenuItemId}: {ErrorMessage}",
                message.MenuItemId, result.ErrorMessage);
        }
    }
}
