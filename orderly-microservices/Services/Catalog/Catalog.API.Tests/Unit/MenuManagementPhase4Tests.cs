using System.Text;
using BuildingBlocks.Storage;
using Catalog.API.Application.Abstractions;
using Catalog.API.Features.MenuItems.Common;
using Catalog.API.Features.MenuItems.UploadMenuItemImage;
using Catalog.API.Models;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Catalog.API.Tests.Unit;

public sealed class MenuManagementPhase4Tests
{
    private static IFormFile CreateMockFormFile(
        byte[] content,
        string fileName = "test.png",
        string contentType = "image/png")
    {
        MemoryStream stream = new(content);
        FormFile file = new(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        return file;
    }

    private static byte[] CreateDummyPngBytes(int width = 100, int height = 100)
    {
        using MemoryStream ms = new();
        using BinaryWriter bw = new(ms);

        // PNG signature
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk: 13 bytes length
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(13));
        bw.Write(Encoding.ASCII.GetBytes("IHDR"));
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width));
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height));
        bw.Write((byte)8); // bit depth
        bw.Write((byte)2); // color type (RGB)
        bw.Write((byte)0); // compression
        bw.Write((byte)0); // filter
        bw.Write((byte)0); // interlace
        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // CRC dummy

        return ms.ToArray();
    }

    private static byte[] CreateDummyJpegBytes(int width = 100, int height = 100)
    {
        using MemoryStream ms = new();
        using BinaryWriter bw = new(ms);

        // SOI
        bw.Write(new byte[] { 0xFF, 0xD8, 0xFF });
        // APP0 marker
        bw.Write((byte)0xE0);
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)16));
        bw.Write(Encoding.ASCII.GetBytes("JFIF\0"));
        bw.Write(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 });

        // SOF0 marker
        bw.Write(new byte[] { 0xFF, 0xC0 });
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)17));
        bw.Write((byte)8); // precision
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)height));
        bw.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)width));
        bw.Write(new byte[] { 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1 }); // 3 components

        // EOI
        bw.Write(new byte[] { 0xFF, 0xD9 });

        return ms.ToArray();
    }

    [Fact]
    public async Task ImageHeaderValidator_AcceptsValidPng()
    {
        byte[] bytes = CreateDummyPngBytes(150, 200);
        IFormFile file = CreateMockFormFile(bytes, "photo.png", "image/png");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeTrue();
        result.DetectedFormat.Should().Be("PNG");
        result.Width.Should().Be(150);
        result.Height.Should().Be(200);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImageHeaderValidator_AcceptsValidJpeg()
    {
        byte[] bytes = CreateDummyJpegBytes(300, 250);
        IFormFile file = CreateMockFormFile(bytes, "photo.jpg", "image/jpeg");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeTrue();
        result.DetectedFormat.Should().Be("JPEG");
        result.Width.Should().Be(300);
        result.Height.Should().Be(250);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImageHeaderValidator_RejectsDisallowedExtension()
    {
        byte[] bytes = CreateDummyPngBytes();
        IFormFile file = CreateMockFormFile(bytes, "photo.exe", "image/png");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("File extension '.exe' is not allowed");
    }

    [Fact]
    public async Task ImageHeaderValidator_RejectsDisallowedMimeType()
    {
        byte[] bytes = CreateDummyPngBytes();
        IFormFile file = CreateMockFormFile(bytes, "photo.png", "application/pdf");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Content-Type 'application/pdf' is not permitted");
    }

    [Fact]
    public async Task ImageHeaderValidator_RejectsMismatchedMagicBytes()
    {
        byte[] fakeBytes = Encoding.UTF8.GetBytes("This is not an image file at all!");
        IFormFile file = CreateMockFormFile(fakeBytes, "photo.png", "image/png");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Image magic bytes do not match");
    }

    [Fact]
    public async Task ImageHeaderValidator_RejectsDimensionsExceedingMaxLimit()
    {
        byte[] bytes = CreateDummyPngBytes(8001, 100);
        IFormFile file = CreateMockFormFile(bytes, "huge.png", "image/png");

        ImageValidationResult result = await ImageHeaderValidator.ValidateAsync(file);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceed maximum allowed dimensions");
    }

    [Fact]
    public void UploadMenuItemImageValidator_ValidatesCommand()
    {
        UploadMenuItemImageValidator validator = new();

        UploadMenuItemImageCommand validCommand = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateMockFormFile(CreateDummyPngBytes()));

        TestValidationResult<UploadMenuItemImageCommand> result = validator.TestValidate(validCommand);
        result.ShouldNotHaveAnyValidationErrors();

        UploadMenuItemImageCommand invalidCommand = new(
            Guid.Empty,
            Guid.Empty,
            null!);

        TestValidationResult<UploadMenuItemImageCommand> invalidResult = validator.TestValidate(invalidCommand);
        invalidResult.ShouldHaveValidationErrorFor(x => x.RestaurantId);
        invalidResult.ShouldHaveValidationErrorFor(x => x.MenuItemId);
        invalidResult.ShouldHaveValidationErrorFor(x => x.File);
    }

    [Fact]
    public async Task UploadMenuItemImageHandler_ThrowsForbidden_WhenTenantMismatch()
    {
        CatalogDbContext db = CreateInMemoryDbContext();
        IBlobStorageService blobStorage = Substitute.For<IBlobStorageService>();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        IOptions<BlobStorageOptions> options = Options.Create(new BlobStorageOptions());
        ILogger<UploadMenuItemImageHandler> logger = NullLogger<UploadMenuItemImageHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        currentUser.When(c => c.EnsureTenantAccess(restaurantId))
            .Do(_ => throw new BuildingBlocks.Exceptions.ForbiddenException("Tenant mismatch"));

        UploadMenuItemImageHandler handler = new(db, blobStorage, notifier, currentUser, options, logger);

        UploadMenuItemImageCommand command = new(
            restaurantId,
            Guid.NewGuid(),
            CreateMockFormFile(CreateDummyPngBytes()));

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<BuildingBlocks.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task UploadMenuItemImageHandler_ThrowsNotFound_WhenMenuItemDoesNotExist()
    {
        CatalogDbContext db = CreateInMemoryDbContext();
        IBlobStorageService blobStorage = Substitute.For<IBlobStorageService>();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        IOptions<BlobStorageOptions> options = Options.Create(new BlobStorageOptions());
        ILogger<UploadMenuItemImageHandler> logger = NullLogger<UploadMenuItemImageHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid missingId = Guid.NewGuid();

        UploadMenuItemImageHandler handler = new(db, blobStorage, notifier, currentUser, options, logger);

        UploadMenuItemImageCommand command = new(
            restaurantId,
            missingId,
            CreateMockFormFile(CreateDummyPngBytes()));

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<BuildingBlocks.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task UploadMenuItemImageHandler_UploadsBlob_UpdatesImageUrl_AndCleansPreviousBlob()
    {
        CatalogDbContext db = CreateInMemoryDbContext();
        IBlobStorageService blobStorage = Substitute.For<IBlobStorageService>();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        IOptions<BlobStorageOptions> options = Options.Create(new BlobStorageOptions
        {
            ContainerName = "menu-assets",
            PublicBaseUrl = "http://localhost:10000/devstoreaccount1",
        });
        ILogger<UploadMenuItemImageHandler> logger = NullLogger<UploadMenuItemImageHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        MenuItem item = new()
        {
            Id = itemId,
            RestaurantId = restaurantId,
            Name = "Signature Burger",
            Description = "Delicious burger",
            BasePrice = 12.99m,
            IsAvailable = true,
            AvailabilityStatus = AvailabilityStatus.Available,
            ImageUrl = $"http://localhost:10000/devstoreaccount1/menu-assets/restaurants/{restaurantId}/menu-items/{itemId}/old.png",
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        Uri expectedNewUri = new($"http://localhost:10000/devstoreaccount1/menu-assets/restaurants/{restaurantId}/menu-items/{itemId}/new.png");
        blobStorage.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedNewUri);

        UploadMenuItemImageHandler handler = new(db, blobStorage, notifier, currentUser, options, logger);

        UploadMenuItemImageCommand command = new(
            restaurantId,
            itemId,
            CreateMockFormFile(CreateDummyPngBytes(), "new-burger.png", "image/png"));

        UploadMenuItemImageResult result = await handler.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be(expectedNewUri.ToString());
        item.ImageUrl.Should().Be(expectedNewUri.ToString());

        await notifier.Received(1).SaveAndNotifyAsync(item, MenuItemChangeType.Updated, Arg.Any<CancellationToken>());
        await blobStorage.Received(1).DeleteAsync("menu-assets", Arg.Is<string>(s => s.Contains("old.png")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadMenuItemImageHandler_RollsBackUploadedBlob_WhenSaveFails()
    {
        CatalogDbContext db = CreateInMemoryDbContext();
        IBlobStorageService blobStorage = Substitute.For<IBlobStorageService>();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        IOptions<BlobStorageOptions> options = Options.Create(new BlobStorageOptions
        {
            ContainerName = "menu-assets",
            PublicBaseUrl = "http://localhost:10000/devstoreaccount1",
        });
        ILogger<UploadMenuItemImageHandler> logger = NullLogger<UploadMenuItemImageHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        MenuItem item = new()
        {
            Id = itemId,
            RestaurantId = restaurantId,
            Name = "Signature Burger",
            Description = "Delicious burger",
            BasePrice = 12.99m,
            IsAvailable = true,
            AvailabilityStatus = AvailabilityStatus.Available,
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        Uri expectedNewUri = new($"http://localhost:10000/devstoreaccount1/menu-assets/restaurants/{restaurantId}/menu-items/{itemId}/new.png");
        blobStorage.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedNewUri);

        notifier.SaveAndNotifyAsync(item, MenuItemChangeType.Updated, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB commit failure"));

        UploadMenuItemImageHandler handler = new(db, blobStorage, notifier, currentUser, options, logger);

        UploadMenuItemImageCommand command = new(
            restaurantId,
            itemId,
            CreateMockFormFile(CreateDummyPngBytes(), "new-burger.png", "image/png"));

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify blob cleanup was invoked
        await blobStorage.Received(1).DeleteAsync("menu-assets", Arg.Is<string>(p => p.Contains($"restaurants/{restaurantId}/menu-items/{itemId}/")), Arg.Any<CancellationToken>());
    }

    private static CatalogDbContext CreateInMemoryDbContext()
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollection services = new();
        services.AddHttpContextAccessor();
        Microsoft.Extensions.DependencyInjection.ServiceProvider appServiceProvider = services.BuildServiceProvider();

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: $"catalog-unit-{Guid.NewGuid()}")
            .AddInterceptors(new BuildingBlocks.Entities.Interceptors.AuditableEntityInterceptor())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning,
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseApplicationServiceProvider(appServiceProvider)
            .Options;

        return new CatalogDbContext(options);
    }
}
