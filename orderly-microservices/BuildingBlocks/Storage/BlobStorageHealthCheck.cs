using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Storage;

/// <summary>
/// Health check that verifies connectivity to the blob storage service and container availability.
/// </summary>
public sealed class BlobStorageHealthCheck(
    BlobServiceClient blobServiceClient,
    IOptions<BlobStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string containerName = options.Value.ContainerName;
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            bool exists = await containerClient.ExistsAsync(cancellationToken);

            return exists
                ? HealthCheckResult.Healthy($"Blob container '{containerName}' is available.")
                : HealthCheckResult.Degraded($"Blob container '{containerName}' does not exist yet.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Blob storage health check failed.", ex);
        }
    }
}
