using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Storage;

/// <summary>
/// Hosted service that ensures the configured blob container exists with public blob read access on application startup.
/// </summary>
public sealed class BlobStorageContainerInitializer(
    BlobServiceClient blobServiceClient,
    IOptions<BlobStorageOptions> options,
    ILogger<BlobStorageContainerInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.EnsureContainerOnStartup)
        {
            logger.LogInformation("Blob storage container initialization skipped (EnsureContainerOnStartup = false).");
            return;
        }

        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            string containerName = options.Value.ContainerName;
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: timeoutCts.Token);
            logger.LogInformation("Blob storage container '{ContainerName}' ensured with public read access.", containerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to auto-create blob storage container. Health check will report status.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
