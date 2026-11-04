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
        try
        {
            string containerName = options.Value.ContainerName;
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
            logger.LogInformation("Blob storage container '{ContainerName}' ensured with public read access.", containerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to auto-create blob storage container. Health check will report status.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
