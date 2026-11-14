namespace BuildingBlocks.Storage;

public class ObjectStorageSettings
{
    public const string SectionName = "ObjectStorage";

    public string BucketName { get; set; } = "orderly-restaurant-assets";

    /// <summary>MinIO: http://localhost:9000 | AWS: leave empty (SDK default) | R2: https://{accountId}.r2.cloudflarestorage.com</summary>
    public string Endpoint { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = "minioadmin";

    public string SecretKey { get; set; } = "minioadmin";

    /// <summary>Public CDN base URL prepended to object keys for final ImageUrl values.</summary>
    public string PublicCdnBaseUrl { get; set; } = "http://localhost:9000/orderly-restaurant-assets";

    /// <summary>Presigned URL expiry window in minutes. Default: 15.</summary>
    public int PresignedUrlExpiryMinutes { get; set; } = 15;

    /// <summary>Max upload size in bytes enforced in the presigned URL policy. Default: 5MB.</summary>
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Set to true for MinIO and R2 (force path style: http://endpoint/bucket/key).</summary>
    public bool ForcePathStyle { get; set; } = true;
}
