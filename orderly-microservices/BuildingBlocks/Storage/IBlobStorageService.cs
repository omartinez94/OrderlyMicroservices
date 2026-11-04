namespace BuildingBlocks.Storage;

/// <summary>
/// Provider-agnostic blob storage service contract.
/// Decouples handlers from concrete cloud SDKs.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a stream to the designated container and blob path, returning the publicly reachable <see cref="Uri"/>.
    /// </summary>
    Task<Uri> UploadAsync(
        string containerName,
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob if it exists. Returns <c>true</c> if deleted; <c>false</c> if it did not exist.
    /// </summary>
    Task<bool> DeleteAsync(
        string containerName,
        string blobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream to the blob content, or returns <c>null</c> if the blob does not exist.
    /// </summary>
    Task<Stream?> OpenReadAsync(
        string containerName,
        string blobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the public or emulator-accessible <see cref="Uri"/> for a blob path.
    /// </summary>
    Uri GetBlobUri(string containerName, string blobPath);

    /// <summary>
    /// Enumerates blob item paths matching an optional prefix.
    /// </summary>
    IAsyncEnumerable<string> ListBlobsAsync(
        string containerName,
        string? prefix = null,
        CancellationToken cancellationToken = default);
}
