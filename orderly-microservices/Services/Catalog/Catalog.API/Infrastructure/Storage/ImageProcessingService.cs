using BuildingBlocks.Storage;
using Catalog.API.Caching;
using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Catalog.API.Infrastructure.Storage;

public interface IImageProcessingService
{
    Task<ImageProcessingResult> ProcessMenuItemImageAsync(
        Guid restaurantId,
        Guid menuItemId,
        string sourceObjectKey,
        CancellationToken cancellationToken = default);
}

public sealed record ImageProcessingResult(
    bool Success,
    string? StandardUrl = null,
    string? ThumbnailUrl = null,
    string? ErrorMessage = null);

public sealed class ImageProcessingService(
    CatalogDbContext dbContext,
    IObjectStorageService objectStorage,
    ICatalogCache cache,
    ILogger<ImageProcessingService> logger) : IImageProcessingService
{
    public async Task<ImageProcessingResult> ProcessMenuItemImageAsync(
        Guid restaurantId,
        Guid menuItemId,
        string sourceObjectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);

        logger.LogInformation(
            "Starting image processing for MenuItem {MenuItemId} (Restaurant {RestaurantId}, Key: {ObjectKey})",
            menuItemId, restaurantId, sourceObjectKey);

        await using Stream? sourceStream = await objectStorage.OpenReadAsync(sourceObjectKey, cancellationToken)
            .ConfigureAwait(false);

        if (sourceStream is null)
        {
            logger.LogWarning("Source image object {ObjectKey} was not found in storage", sourceObjectKey);
            return new ImageProcessingResult(false, ErrorMessage: $"Object '{sourceObjectKey}' not found in storage.");
        }

        try
        {
            using Image image = await Image.LoadAsync(sourceStream, cancellationToken).ConfigureAwait(false);

            int lastDot = sourceObjectKey.LastIndexOf('.');
            string baseKey = lastDot > 0 ? sourceObjectKey[..lastDot] : sourceObjectKey;
            string standardKey = $"{baseKey}-standard.webp";
            string thumbKey = $"{baseKey}-thumb.webp";

            // 1. Generate standard WebP (max 800x800)
            using (Image standard = image.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(800, 800),
                Mode = ResizeMode.Max
            })))
            {
                using MemoryStream standardStream = new();
                await standard.SaveAsWebpAsync(standardStream, new WebpEncoder { Quality = 85 }, cancellationToken)
                    .ConfigureAwait(false);
                standardStream.Position = 0;
                await objectStorage.UploadAsync(standardKey, standardStream, "image/webp", cancellationToken)
                    .ConfigureAwait(false);
            }

            // 2. Generate thumbnail WebP (150x150 cropped)
            using (Image thumb = image.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(150, 150),
                Mode = ResizeMode.Crop
            })))
            {
                using MemoryStream thumbStream = new();
                await thumb.SaveAsWebpAsync(thumbStream, new WebpEncoder { Quality = 80 }, cancellationToken)
                    .ConfigureAwait(false);
                thumbStream.Position = 0;
                await objectStorage.UploadAsync(thumbKey, thumbStream, "image/webp", cancellationToken)
                    .ConfigureAwait(false);
            }

            string standardUrl = objectStorage.GetPublicUrl(standardKey);
            string thumbUrl = objectStorage.GetPublicUrl(thumbKey);

            MenuItem? menuItem = await dbContext.MenuItems
                .FirstOrDefaultAsync(m => m.Id == menuItemId && m.RestaurantId == restaurantId, cancellationToken)
                .ConfigureAwait(false);

            if (menuItem != null)
            {
                menuItem.ImageUrl = standardUrl;
                menuItem.ThumbnailUrl = thumbUrl;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await cache.InvalidateMenuAsync(restaurantId, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation(
                "Image processing completed for MenuItem {MenuItemId}. Standard: {StandardUrl}, Thumb: {ThumbUrl}",
                menuItemId, standardUrl, thumbUrl);

            return new ImageProcessingResult(true, StandardUrl: standardUrl, ThumbnailUrl: thumbUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process image for MenuItem {MenuItemId} from object {ObjectKey}",
                menuItemId, sourceObjectKey);
            return new ImageProcessingResult(false, ErrorMessage: ex.Message);
        }
    }
}
