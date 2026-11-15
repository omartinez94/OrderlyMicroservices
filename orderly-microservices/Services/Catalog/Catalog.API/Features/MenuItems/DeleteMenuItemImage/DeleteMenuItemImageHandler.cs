using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.API.Features.MenuItems.DeleteMenuItemImage;

internal sealed class DeleteMenuItemImageHandler(
    CatalogDbContext dbContext,
    IBlobStorageService blobStorage,
    IMenuItemMutationNotifier notifier,
    ICurrentUser currentUser,
    IOptions<BlobStorageOptions> blobOptions,
    ILogger<DeleteMenuItemImageHandler> logger) : ICommandHandler<DeleteMenuItemImageCommand, DeleteMenuItemImageResult>
{
    public async Task<DeleteMenuItemImageResult> Handle(
        DeleteMenuItemImageCommand command,
        CancellationToken cancellationToken)
    {
        currentUser.EnsureTenantAccess(command.RestaurantId);

        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.MenuItemId, cancellationToken)
            .ConfigureAwait(false);

        if (menuItem is null || menuItem.RestaurantId != command.RestaurantId)
        {
            throw new NotFoundException(nameof(MenuItem), command.MenuItemId);
        }

        string previousImageUrl = menuItem.ImageUrl;
        menuItem.ImageUrl = string.Empty;
        menuItem.ThumbnailUrl = null;
        menuItem.PendingImageKey = null;
        menuItem.ImageUploadStatus = ImageUploadStatus.None;

        await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Updated, cancellationToken)
            .ConfigureAwait(false);

        // Best-effort cleanup of previous image blob
        if (!string.IsNullOrEmpty(previousImageUrl))
        {
            string containerName = blobOptions.Value.ContainerName;
            int idx = previousImageUrl.IndexOf("restaurants/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string previousBlobPath = previousImageUrl.Substring(idx);
                try
                {
                    await blobStorage.DeleteAsync(containerName, previousBlobPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(cleanupEx, "Failed to clean up previous image blob {BlobPath}", previousBlobPath);
                }
            }
        }

        return new DeleteMenuItemImageResult(true);
    }
}
