using System.Runtime.CompilerServices;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Storage;

/// <summary>
/// Implementation of <see cref="IBlobStorageService"/> backed by Azure Blob Storage or the Azurite emulator.
/// </summary>
public sealed class AzureBlobStorageService(
    BlobServiceClient blobServiceClient,
    IOptions<BlobStorageOptions> options) : IBlobStorageService
{
    private readonly BlobStorageOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<Uri> UploadAsync(
        string containerName,
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobPath);

        BlobUploadOptions uploadOptions = new()
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
            },
        };

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await blobClient.UploadAsync(content, uploadOptions, cancellationToken);
        return GetBlobUri(containerName, blobPath);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string containerName,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobPath);

        Azure.Response<bool> response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);

        return response.Value;
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(
        string containerName,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobPath);

        bool exists = await blobClient.ExistsAsync(cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Uri GetBlobUri(string containerName, string blobPath)
    {
        string normalizedBase = _options.PublicBaseUrl.TrimEnd('/');
        string normalizedContainer = containerName.Trim('/');
        string normalizedPath = blobPath.TrimStart('/');

        string fullUrl = $"{normalizedBase}/{normalizedContainer}/{normalizedPath}";
        return new Uri(fullUrl, UriKind.Absolute);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListBlobsAsync(
        string containerName,
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await foreach (BlobItem item in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            yield return item.Name;
        }
    }
}