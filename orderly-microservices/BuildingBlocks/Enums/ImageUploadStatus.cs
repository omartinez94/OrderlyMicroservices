namespace BuildingBlocks.Enums;

public enum ImageUploadStatus
{
    /// <summary>No image upload initiated. MenuItem.ImageUrl may be empty or a legacy URL.</summary>
    None = 0,
    /// <summary>Presigned URL issued. Client is expected to upload. PendingImageKey is set.</summary>
    Pending = 1,
    /// <summary>Upload confirmed. ImageUrl reflects the CDN URL. PendingImageKey is cleared.</summary>
    Confirmed = 2,
}
