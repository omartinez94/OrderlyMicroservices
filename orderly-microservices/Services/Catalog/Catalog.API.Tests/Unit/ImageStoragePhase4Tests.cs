using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Enums;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Storage;
using Catalog.API.Data;
using Catalog.API.Features.Media.ConfirmMenuItemImageUpload;
using Catalog.API.Features.Media.GetMenuItemImageUploadUrl;
using Catalog.API.Features.MenuItems.Common;
using Catalog.API.Features.Restaurants.UploadRestaurantImage;
using Catalog.API.Models;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NodaTime;
using Xunit;

namespace Catalog.API.Tests.Unit;

public class ImageStoragePhase4Tests
{
    private readonly GetMenuItemImageUploadUrlValidator _getMenuUploadValidator = new();
    private readonly ConfirmMenuItemImageUploadValidator _confirmMenuUploadValidator = new();
    private readonly GetRestaurantImageUploadUrlValidator _getRestUploadValidator = new();
    private readonly ConfirmRestaurantImageUploadValidator _confirmRestUploadValidator = new();

    private static CatalogDbContext CreateInMemoryDbContext()
    {
        ServiceCollection services = new();
        services.AddHttpContextAccessor();
        ServiceProvider appServiceProvider = services.BuildServiceProvider();

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: $"catalog-img4-{Guid.NewGuid()}")
            .AddInterceptors(new AuditableEntityInterceptor())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning,
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseApplicationServiceProvider(appServiceProvider)
            .Options;

        return new CatalogDbContext(options);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void GetMenuItemImageUploadUrlValidator_WithValidContentTypes_Passes(string contentType)
    {
        GetMenuItemImageUploadUrlCommand command = new(
            RestaurantId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            ContentType: contentType);

        TestValidationResult<GetMenuItemImageUploadUrlCommand> result = _getMenuUploadValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void GetMenuItemImageUploadUrlValidator_WithInvalidContentTypes_Fails(string contentType)
    {
        GetMenuItemImageUploadUrlCommand command = new(
            RestaurantId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            ContentType: contentType);

        TestValidationResult<GetMenuItemImageUploadUrlCommand> result = _getMenuUploadValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void ConfirmMenuItemImageUploadValidator_WithValidData_Passes()
    {
        ConfirmMenuItemImageUploadCommand command = new(
            RestaurantId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid());

        TestValidationResult<ConfirmMenuItemImageUploadCommand> result = _confirmMenuUploadValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ConfirmMenuItemImageUploadValidator_WithEmptyIds_Fails()
    {
        ConfirmMenuItemImageUploadCommand command = new(
            RestaurantId: Guid.Empty,
            MenuItemId: Guid.Empty);

        TestValidationResult<ConfirmMenuItemImageUploadCommand> result = _confirmMenuUploadValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.RestaurantId);
        result.ShouldHaveValidationErrorFor(x => x.MenuItemId);
    }

    [Theory]
    [InlineData("logo")]
    [InlineData("banner")]
    public void GetRestaurantImageUploadUrlValidator_WithValidAssetType_Passes(string assetType)
    {
        GetRestaurantImageUploadUrlCommand command = new(
            RestaurantId: Guid.NewGuid(),
            AssetType: assetType,
            ContentType: "image/webp");

        TestValidationResult<GetRestaurantImageUploadUrlCommand> result = _getRestUploadValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("avatar")]
    [InlineData("menu")]
    [InlineData("")]
    public void GetRestaurantImageUploadUrlValidator_WithInvalidAssetType_Fails(string assetType)
    {
        GetRestaurantImageUploadUrlCommand command = new(
            RestaurantId: Guid.NewGuid(),
            AssetType: assetType,
            ContentType: "image/webp");

        TestValidationResult<GetRestaurantImageUploadUrlCommand> result = _getRestUploadValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AssetType);
    }

    [Fact]
    public void ConfirmRestaurantImageUploadValidator_WithMatchingPrefix_Passes()
    {
        Guid restaurantId = Guid.NewGuid();
        ConfirmRestaurantImageUploadCommand command = new(
            RestaurantId: restaurantId,
            AssetType: "logo",
            ObjectKey: $"restaurants/{restaurantId}/assets/logo-abc123.png");

        TestValidationResult<ConfirmRestaurantImageUploadCommand> result = _confirmRestUploadValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ConfirmRestaurantImageUploadValidator_WithMismatchedKeyPrefix_Fails()
    {
        Guid restaurantId = Guid.NewGuid();
        ConfirmRestaurantImageUploadCommand command = new(
            RestaurantId: restaurantId,
            AssetType: "logo",
            ObjectKey: $"restaurants/{Guid.NewGuid()}/assets/logo-abc123.png");

        TestValidationResult<ConfirmRestaurantImageUploadCommand> result = _confirmRestUploadValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ObjectKey);
    }

    [Fact]
    public async Task GetMenuItemImageUploadUrlHandler_WhenItemExists_GeneratesPresignedUrlAndSetsPending()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Birria Tacos",
            BasePrice = 140.0m,
            ImageUploadStatus = ImageUploadStatus.None
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IObjectStorageService objectStorage = Substitute.For<IObjectStorageService>();
        objectStorage.GeneratePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.orderly.local/upload-presigned-url");

        IOptions<ObjectStorageSettings> settings = Options.Create(new ObjectStorageSettings
        {
            PresignedUrlExpiryMinutes = 20
        });

        IClock clock = Substitute.For<IClock>();
        Instant now = Instant.FromUtc(2026, 11, 14, 12, 0);
        clock.GetCurrentInstant().Returns(now);

        GetMenuItemImageUploadUrlHandler handler = new(dbContext, objectStorage, settings, clock);

        GetMenuItemImageUploadUrlResult result = await handler.Handle(
            new GetMenuItemImageUploadUrlCommand(restaurantId, menuItemId, "image/webp"),
            CancellationToken.None);

        Assert.Equal("https://storage.orderly.local/upload-presigned-url", result.PresignedUrl);
        Assert.StartsWith($"restaurants/{restaurantId}/menu-items/{menuItemId}/", result.ObjectKey);
        Assert.EndsWith(".webp", result.ObjectKey);
        Assert.Equal(now.Plus(Duration.FromMinutes(20)), result.ExpiresAt);

        MenuItem? updatedItem = await dbContext.MenuItems.FindAsync(menuItemId);
        Assert.NotNull(updatedItem);
        Assert.Equal(ImageUploadStatus.Pending, updatedItem.ImageUploadStatus);
        Assert.Equal(result.ObjectKey, updatedItem.PendingImageKey);
    }

    [Fact]
    public async Task ConfirmMenuItemImageUploadHandler_WhenNotPending_ThrowsBadRequest()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Birria Tacos",
            BasePrice = 140.0m,
            ImageUploadStatus = ImageUploadStatus.None // Not pending
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IObjectStorageService objectStorage = Substitute.For<IObjectStorageService>();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        IOutboxPublisher outbox = Substitute.For<IOutboxPublisher>();
        ILogger<ConfirmMenuItemImageUploadHandler> logger = NullLogger<ConfirmMenuItemImageUploadHandler>.Instance;

        ConfirmMenuItemImageUploadHandler handler = new(dbContext, objectStorage, notifier, outbox, logger);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new ConfirmMenuItemImageUploadCommand(restaurantId, menuItemId), CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmMenuItemImageUploadHandler_WhenObjectNotFoundInStorage_ThrowsBadRequest()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();
        string pendingKey = $"restaurants/{restaurantId}/menu-items/{menuItemId}/image.webp";

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Birria Tacos",
            BasePrice = 140.0m,
            ImageUploadStatus = ImageUploadStatus.Pending,
            PendingImageKey = pendingKey
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IObjectStorageService objectStorage = Substitute.For<IObjectStorageService>();
        objectStorage.ObjectExistsAsync(pendingKey, Arg.Any<CancellationToken>())
            .Returns(false); // Client didn't upload

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        IOutboxPublisher outbox = Substitute.For<IOutboxPublisher>();
        ILogger<ConfirmMenuItemImageUploadHandler> logger = NullLogger<ConfirmMenuItemImageUploadHandler>.Instance;

        ConfirmMenuItemImageUploadHandler handler = new(dbContext, objectStorage, notifier, outbox, logger);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new ConfirmMenuItemImageUploadCommand(restaurantId, menuItemId), CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmMenuItemImageUploadHandler_WhenValid_ConfirmsAndPublishesEvent()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();
        string pendingKey = $"restaurants/{restaurantId}/menu-items/{menuItemId}/image.webp";

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Birria Tacos",
            BasePrice = 140.0m,
            ImageUploadStatus = ImageUploadStatus.Pending,
            PendingImageKey = pendingKey
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IObjectStorageService objectStorage = Substitute.For<IObjectStorageService>();
        objectStorage.ObjectExistsAsync(pendingKey, Arg.Any<CancellationToken>()).Returns(true);
        objectStorage.GetPublicUrl(pendingKey).Returns($"https://cdn.orderly.com/{pendingKey}");

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        IOutboxPublisher outbox = Substitute.For<IOutboxPublisher>();
        ILogger<ConfirmMenuItemImageUploadHandler> logger = NullLogger<ConfirmMenuItemImageUploadHandler>.Instance;

        ConfirmMenuItemImageUploadHandler handler = new(dbContext, objectStorage, notifier, outbox, logger);

        ConfirmMenuItemImageUploadResult result = await handler.Handle(
            new ConfirmMenuItemImageUploadCommand(restaurantId, menuItemId),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"https://cdn.orderly.com/{pendingKey}", result.ImageUrl);

        Assert.Equal(ImageUploadStatus.Confirmed, item.ImageUploadStatus);
        Assert.Null(item.PendingImageKey);
        Assert.Equal($"https://cdn.orderly.com/{pendingKey}", item.ImageUrl);

        await outbox.Received(1).PublishAsync(
            Arg.Is<MenuItemImageUploadedIntegrationEvent>(e =>
                e.RestaurantId == restaurantId &&
                e.MenuItemId == menuItemId &&
                e.ImageUrl == $"https://cdn.orderly.com/{pendingKey}" &&
                e.ObjectKey == pendingKey),
            Arg.Any<CancellationToken>());

        await notifier.Received(1).SaveAndNotifyAsync(
            item,
            MenuItemChangeType.Updated,
            Arg.Any<CancellationToken>());
    }
}
