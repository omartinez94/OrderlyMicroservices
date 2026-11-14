using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Storage;

public static class BlobStorageServiceExtensions
{
    /// <summary>
    /// Registers provider-agnostic blob storage services, Azure Blob Storage client, container initialization, and health checks.
    /// Fails fast at startup if an unsupported provider is configured.
    /// </summary>
    public static IServiceCollection AddBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(BlobStorageOptions.SectionName);

        services.AddOptions<BlobStorageOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        BlobStorageOptions options = section.Get<BlobStorageOptions>() ?? new();

        if (!string.Equals(options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Blob storage provider '{options.Provider}' is not supported in v1. Supported providers: 'AzureBlob'.");
        }

        services.AddSingleton(_ => new BlobServiceClient(options.ConnectionString));
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddHostedService<BlobStorageContainerInitializer>();
        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>("blob_storage", tags: ["ready", "storage"]);

        return services;
    }

    /// <summary>
    /// Registers S3-compatible object storage service (MinIO, Cloudflare R2, AWS S3).
    /// </summary>
    public static IServiceCollection AddObjectStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ObjectStorageSettings>(configuration.GetSection(ObjectStorageSettings.SectionName));
        services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
        return services;
    }
}
