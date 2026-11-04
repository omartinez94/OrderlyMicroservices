using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

/// <summary>
/// Periodically scans the blob container for orphaned menu item images that are no longer
/// referenced by any <see cref="MenuItem"/> in the database and are older than 24 hours.
/// </summary>
public sealed class MenuItemImageOrphanSweepHostedService(
    IServiceScopeFactory scopeFactory,
    IBlobStorageService blobStorage,
    IOptions<BlobStorageOptions> blobOptions,
    ILogger<MenuItemImageOrphanSweepHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionThreshold = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepOrphansAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
    }

    public async Task SweepOrphansAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting menu item image orphan blob cleanup sweep...");

            string containerName = blobOptions.Value.ContainerName;

            using IServiceScope scope = scopeFactory.CreateScope();
            CatalogDbContext dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            List<string> activeImageUrls = await dbContext.MenuItems
                .Where(m => m.ImageUrl != null)
                .Select(m => m.ImageUrl!)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            HashSet<string> activeBlobPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (string url in activeImageUrls)
            {
                int idx = url.IndexOf("restaurants/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    activeBlobPaths.Add(url.Substring(idx));
                }
            }

            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(RetentionThreshold);
            int deletedCount = 0;

            await foreach (string blobName in blobStorage.ListBlobsAsync(containerName, "restaurants/", cancellationToken).ConfigureAwait(false))
            {
                if (activeBlobPaths.Contains(blobName))
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(blobName);
                if (long.TryParse(fileName, out long unixMs))
                {
                    DateTimeOffset blobTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                    if (blobTimestamp < cutoff)
                    {
                        bool deleted = await blobStorage.DeleteAsync(containerName, blobName, cancellationToken).ConfigureAwait(false);
                        if (deleted)
                        {
                            deletedCount++;
                        }
                    }
                }
            }

            logger.LogInformation("Completed menu item image orphan sweep. Removed {Count} orphaned blobs.", deletedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during menu item image orphan blob cleanup sweep");
        }
    }
}
