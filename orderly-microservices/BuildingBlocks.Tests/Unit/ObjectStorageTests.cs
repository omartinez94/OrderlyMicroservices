using Amazon.S3;
using Amazon.S3.Model;
using BuildingBlocks.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Unit;

public sealed class ObjectStorageTests
{
    [Fact]
    public void ObjectStorageSettings_HasSensibleDefaults()
    {
        ObjectStorageSettings settings = new();

        settings.BucketName.Should().Be("orderly-restaurant-assets");
        settings.Endpoint.Should().Be("http://localhost:9000");
        settings.AccessKey.Should().Be("minioadmin");
        settings.SecretKey.Should().Be("minioadmin");
        settings.PublicCdnBaseUrl.Should().Be("http://localhost:9000/orderly-restaurant-assets");
        settings.PresignedUrlExpiryMinutes.Should().Be(15);
        settings.MaxUploadBytes.Should().Be(5 * 1024 * 1024);
        settings.ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public void GetPublicUrl_FormatsExpectedUrl_HandlingLeadingSlashes()
    {
        ObjectStorageSettings settings = new()
        {
            PublicCdnBaseUrl = "https://cdn.orderly.com/assets/",
        };
        IOptions<ObjectStorageSettings> optionsWrapper = Options.Create(settings);
        IAmazonS3 mockS3 = Substitute.For<IAmazonS3>();

        using S3ObjectStorageService service = new(optionsWrapper, mockS3);

        string url = service.GetPublicUrl("/restaurants/123/items/burger.webp");

        url.Should().Be("https://cdn.orderly.com/assets/restaurants/123/items/burger.webp");
    }

    [Fact]
    public async Task GeneratePresignedUploadUrlAsync_DelegatesToS3Client()
    {
        ObjectStorageSettings settings = new()
        {
            BucketName = "test-bucket",
            PresignedUrlExpiryMinutes = 20,
        };
        IOptions<ObjectStorageSettings> optionsWrapper = Options.Create(settings);
        IAmazonS3 mockS3 = Substitute.For<IAmazonS3>();

        mockS3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>())
            .Returns("https://s3.amazonaws.com/test-bucket/item.webp?signed=token");

        using S3ObjectStorageService service = new(optionsWrapper, mockS3);

        string presignedUrl = await service.GeneratePresignedUploadUrlAsync("item.webp", "image/webp");

        presignedUrl.Should().Be("https://s3.amazonaws.com/test-bucket/item.webp?signed=token");
        mockS3.Received(1).GetPreSignedURL(Arg.Is<GetPreSignedUrlRequest>(r =>
            r.BucketName == "test-bucket" &&
            r.Key == "item.webp" &&
            r.ContentType == "image/webp" &&
            r.Verb == HttpVerb.PUT));
    }

    [Fact]
    public async Task ObjectExistsAsync_ReturnsTrue_WhenObjectFound()
    {
        ObjectStorageSettings settings = new() { BucketName = "test-bucket" };
        IOptions<ObjectStorageSettings> optionsWrapper = Options.Create(settings);
        IAmazonS3 mockS3 = Substitute.For<IAmazonS3>();

        mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse());

        using S3ObjectStorageService service = new(optionsWrapper, mockS3);

        bool exists = await service.ObjectExistsAsync("items/tacos.webp");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ObjectExistsAsync_ReturnsFalse_WhenNotFound()
    {
        ObjectStorageSettings settings = new() { BucketName = "test-bucket" };
        IOptions<ObjectStorageSettings> optionsWrapper = Options.Create(settings);
        IAmazonS3 mockS3 = Substitute.For<IAmazonS3>();

        mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectMetadataResponse>>(_ => throw new AmazonS3Exception("Not Found")
            {
                StatusCode = System.Net.HttpStatusCode.NotFound,
            });

        using S3ObjectStorageService service = new(optionsWrapper, mockS3);

        bool exists = await service.ObjectExistsAsync("items/missing.webp");

        exists.Should().BeFalse();
    }

    [Fact]
    public void AddObjectStorage_RegistersServiceAndSettings()
    {
        ServiceCollection services = new();
        Dictionary<string, string?> inMemorySettings = new()
        {
            ["ObjectStorage:BucketName"] = "my-custom-bucket",
            ["ObjectStorage:Endpoint"] = "http://minio:9000",
            ["ObjectStorage:AccessKey"] = "testkey",
            ["ObjectStorage:SecretKey"] = "testsecret",
            ["ObjectStorage:PublicCdnBaseUrl"] = "https://cdn.myrestaurant.com",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddObjectStorage(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        IObjectStorageService storageService = provider.GetRequiredService<IObjectStorageService>();
        storageService.Should().NotBeNull();
        storageService.GetPublicUrl("test.png").Should().Be("https://cdn.myrestaurant.com/test.png");
    }
}
