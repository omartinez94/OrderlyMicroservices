using Azure.Storage.Blobs;
using BuildingBlocks.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Tests.Unit;

public sealed class BlobStorageTests
{
    [Fact]
    public void BlobStorageOptions_HasSensibleDefaults()
    {
        BlobStorageOptions options = new();

        options.Provider.Should().Be("AzureBlob");
        options.ConnectionString.Should().Be("UseDevelopmentStorage=true");
        options.PublicBaseUrl.Should().Be("http://localhost:10000/devstoreaccount1");
        options.ContainerName.Should().Be("menu-assets");
    }

    [Fact]
    public void GetBlobUri_FormatsExpectedUrl_HandlingSlashesCleanly()
    {
        BlobStorageOptions options = new()
        {
            PublicBaseUrl = "http://localhost:10000/devstoreaccount1/",
            ContainerName = "menu-assets",
        };
        IOptions<BlobStorageOptions> optionsWrapper = Options.Create(options);

        // Dummy connection string for client initialization in test
        BlobServiceClient client = new("UseDevelopmentStorage=true");
        AzureBlobStorageService service = new(client, optionsWrapper);

        Uri uri = service.GetBlobUri("menu-assets", "/restaurants/123/items/456.jpg");

        uri.ToString().Should().Be("http://localhost:10000/devstoreaccount1/menu-assets/restaurants/123/items/456.jpg");
    }

    [Fact]
    public void AddBlobStorage_WhenProviderIsUnsupported_ThrowsNotSupportedException()
    {
        ServiceCollection services = new();
        Dictionary<string, string?> inMemorySettings = new()
        {
            ["BlobStorage:Provider"] = "S3",
            ["BlobStorage:ConnectionString"] = "s3://my-bucket",
            ["BlobStorage:PublicBaseUrl"] = "https://cdn.example.com",
            ["BlobStorage:ContainerName"] = "assets",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        Action act = () => services.AddBlobStorage(configuration);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*not supported in v1*");
    }

    [Fact]
    public void AddBlobStorage_WhenConfiguredForAzureBlob_RegistersServiceDescriptor()
    {
        ServiceCollection services = new();
        Dictionary<string, string?> inMemorySettings = new()
        {
            ["BlobStorage:Provider"] = "AzureBlob",
            ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["BlobStorage:PublicBaseUrl"] = "http://localhost:10000/devstoreaccount1",
            ["BlobStorage:ContainerName"] = "menu-assets",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddBlobStorage(configuration);

        ServiceDescriptor? descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IBlobStorageService));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(AzureBlobStorageService));
    }
}
