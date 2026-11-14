using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Storage;

/// <summary>
/// Configuration options for object / blob storage.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Storage provider identifier. Supported values: "AzureBlob".
    /// Unsupported values fail fast at startup.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Provider { get; set; } = "AzureBlob";

    /// <summary>
    /// Connection string for the storage account or Azurite emulator.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    /// <summary>
    /// Public base URL for constructing browser-accessible blob URIs (e.g. CDN or Azurite host).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string PublicBaseUrl { get; set; } = "http://localhost:10000/devstoreaccount1";

    /// <summary>
    /// Default container name for assets.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ContainerName { get; set; } = "menu-assets";

    /// <summary>
    /// Whether to ensure the blob container exists at host startup. Default: true.
    /// </summary>
    public bool EnsureContainerOnStartup { get; set; } = true;
}
