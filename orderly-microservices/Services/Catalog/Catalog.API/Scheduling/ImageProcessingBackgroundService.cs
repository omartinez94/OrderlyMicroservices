using BuildingBlocks.Enums;
using Catalog.API.Data;
using Catalog.API.Infrastructure.Storage;
using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catalog.API.Scheduling;

/// <summary>
/// Background worker that scans for confirmed <see cref="MenuItem"/> records that lack
/// thumbnail WebP variants (e.g. from unprocessed uploads or migrations) and processes them.
/// </summary>
public sealed class ImageProcessingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageProcessingBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessPendingThumbnailsAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
    }

    public async Task ProcessPendingThumbnailsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            CatalogDbContext dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            IImageProcessingService processingService = scope.ServiceProvider.GetRequiredService<IImageProcessingService>();

            List<MenuItem> unprocessedItems = await dbContext.MenuItems
                .Where(m => m.ImageUploadStatus == ImageUploadStatus.Confirmed
                            && m.ThumbnailUrl == null
                            && !string.IsNullOrEmpty(m.ImageUrl))
                .Take(20)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (unprocessedItems.Count == 0)
            {
                return;
            }

            logger.LogInformation("Found {Count} confirmed menu items pending thumbnail processing", unprocessedItems.Count);

            foreach (MenuItem item in unprocessedItems)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                int idx = item.ImageUrl.IndexOf("restaurants/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    logger.LogWarning("MenuItem {MenuItemId} ImageUrl does not contain storage prefix: {ImageUrl}",
                        item.Id, item.ImageUrl);
                    continue;
                }

                string objectKey = item.ImageUrl[idx..];

                await processingService.ProcessMenuItemImageAsync(
                    item.RestaurantId,
                    item.Id,
                    objectKey,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in ImageProcessingBackgroundService sweep");
        }
    }
}
