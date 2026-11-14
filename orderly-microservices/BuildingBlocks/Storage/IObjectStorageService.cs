namespace BuildingBlocks.Storage;

public interface IObjectStorageService
{
    /// <summary>Generates a presigned PUT URL for direct client upload. Content-Type and size are enforced.</summary>
    Task<string> GeneratePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Returns true if the object key exists in the bucket (used by ConfirmUpload handler).</summary>
    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Returns the public CDN URL for a confirmed object key.</summary>
    string GetPublicUrl(string objectKey);

    /// <summary>Deletes an object by key (used when a MenuItem is deleted or image replaced).</summary>
    Task<bool> DeleteAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Uploads directly to storage (used for server-side processing/thumbnails).</summary>
    Task<string> UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Opens a read-only stream to the object (used by processing workers).</summary>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default);
}
