using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

internal sealed class UploadMenuItemImageHandler(
    CatalogDbContext dbContext,
    IBlobStorageService blobStorage,
    IMenuItemMutationNotifier notifier,
    ICurrentUser currentUser,
    IOptions<BlobStorageOptions> blobOptions,
    ILogger<UploadMenuItemImageHandler> logger) : ICommandHandler<UploadMenuItemImageCommand, UploadMenuItemImageResult>
{
    public async Task<UploadMenuItemImageResult> Handle(
        UploadMenuItemImageCommand command,
        CancellationToken cancellationToken)
    {
        currentUser.EnsureTenantAccess(command.RestaurantId);

        ImageValidationResult validation = await ImageHeaderValidator.ValidateAsync(command.File, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            throw new BadRequestException(validation.ErrorMessage ?? "Invalid image file.");
        }

        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.MenuItemId, cancellationToken)
            .ConfigureAwait(false);

        if (menuItem is null || menuItem.RestaurantId != command.RestaurantId)
        {
            throw new NotFoundException(nameof(MenuItem), command.MenuItemId);
        }

        string containerName = blobOptions.Value.ContainerName;
        string extension = Path.GetExtension(command.File.FileName).ToLowerInvariant();
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string blobPath = $"restaurants/{command.RestaurantId}/menu-items/{command.MenuItemId}/{timestamp}{extension}";

        await using Stream stream = command.File.OpenReadStream();
        Uri uploadedUri = await blobStorage.UploadAsync(
            containerName,
            blobPath,
            stream,
            command.File.ContentType,
            cancellationToken).ConfigureAwait(false);

        string? previousImageUrl = menuItem.ImageUrl;
        menuItem.ImageUrl = uploadedUri.ToString();

        try
        {
            await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Updated, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist menu item image URL. Rolling back uploaded blob {BlobPath}", blobPath);
            try
            {
                await blobStorage.DeleteAsync(containerName, blobPath, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx, "Failed to clean up uploaded blob {BlobPath} after rollback", blobPath);
            }

            throw;
        }

        // Best-effort cleanup of previous image blob
        if (!string.IsNullOrEmpty(previousImageUrl))
        {
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

        return new UploadMenuItemImageResult(Success: true, ImageUrl: menuItem.ImageUrl);
    }
}
