using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Enums;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Storage;
using Catalog.API.Caching;
using Catalog.API.Data;
using Catalog.API.Infrastructure.Storage;
using Catalog.API.Messaging.EventHandlers;
using Catalog.API.Models;
using Catalog.API.Scheduling;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Catalog.API.Tests.Unit;

public class ImageStoragePhase5Tests
{
    private static CatalogDbContext CreateInMemoryDbContext()
    {
        ServiceCollection services = new();
        services.AddHttpContextAccessor();
        ServiceProvider appServiceProvider = services.BuildServiceProvider();

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: $"catalog-img5-{Guid.NewGuid()}")
            .AddInterceptors(new AuditableEntityInterceptor())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning,
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseApplicationServiceProvider(appServiceProvider)
            .Options;

        return new CatalogDbContext(options);
    }

    private static byte[] CreateSampleImageBytes(int width = 200, int height = 200)
    {
        using Image<Rgba32> image = new(width, height);
        using MemoryStream ms = new();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task ProcessMenuItemImageAsync_GeneratesStandardAndThumbWebP_UpdatesMenuItem()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();
        string sourceKey = $"restaurants/{restaurantId}/menu-items/{menuItemId}/original.png";

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Enchiladas Verdes",
            BasePrice = 160.0m,
            ImageUrl = $"https://cdn.orderly.com/{sourceKey}",
            ImageUploadStatus = ImageUploadStatus.Confirmed
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        byte[] sampleBytes = CreateSampleImageBytes(400, 300);

        IObjectStorageService objectStorage = Substitute.For<IObjectStorageService>();
        objectStorage.OpenReadAsync(sourceKey, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(sampleBytes));

        objectStorage.GetPublicUrl(Arg.Is<string>(k => k.EndsWith("-standard.webp")))
            .Returns($"https://cdn.orderly.com/restaurants/{restaurantId}/menu-items/{menuItemId}/original-standard.webp");
        objectStorage.GetPublicUrl(Arg.Is<string>(k => k.EndsWith("-thumb.webp")))
            .Returns($"https://cdn.orderly.com/restaurants/{restaurantId}/menu-items/{menuItemId}/original-thumb.webp");

        ICatalogCache cache = Substitute.For<ICatalogCache>();

        ImageProcessingService service = new(
            dbContext,
            objectStorage,
            cache,
            NullLogger<ImageProcessingService>.Instance);

        ImageProcessingResult result = await service.ProcessMenuItemImageAsync(
            restaurantId,
            menuItemId,
            sourceKey,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.StandardUrl);
        Assert.NotNull(result.ThumbnailUrl);

        // Verify uploads of standard and thumb WebP variants
        await objectStorage.Received(1).UploadAsync(
            Arg.Is<string>(k => k.EndsWith("-standard.webp")),
            Arg.Any<Stream>(),
            "image/webp",
            Arg.Any<CancellationToken>());

        await objectStorage.Received(1).UploadAsync(
            Arg.Is<string>(k => k.EndsWith("-thumb.webp")),
            Arg.Any<Stream>(),
            "image/webp",
            Arg.Any<CancellationToken>());

        // Verify DB was updated
        MenuItem? updated = await dbContext.MenuItems.FindAsync(menuItemId);
        Assert.NotNull(updated);
        Assert.Equal(result.StandardUrl, updated.ImageUrl);
        Assert.Equal(result.ThumbnailUrl, updated.ThumbnailUrl);

        // Verify cache invalidation
        await cache.Received(1).InvalidateMenuAsync(restaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MenuItemImageUploadedIntegrationEventHandler_ConsumesEventAndCallsProcessing()
    {
        IImageProcessingService processingService = Substitute.For<IImageProcessingService>();
        processingService.ProcessMenuItemImageAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageProcessingResult(true, "std.webp", "thumb.webp"));

        MenuItemImageUploadedIntegrationEventHandler handler = new(
            processingService,
            NullLogger<MenuItemImageUploadedIntegrationEventHandler>.Instance);

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();
        string key = $"restaurants/{restaurantId}/menu-items/{menuItemId}/dish.jpg";

        ConsumeContext<MenuItemImageUploadedIntegrationEvent> context = Substitute.For<ConsumeContext<MenuItemImageUploadedIntegrationEvent>>();
        context.Message.Returns(new MenuItemImageUploadedIntegrationEvent
        {
            RestaurantId = restaurantId,
            MenuItemId = menuItemId,
            ImageUrl = $"https://cdn.orderly.com/{key}",
            ObjectKey = key
        });
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);

        await processingService.Received(1).ProcessMenuItemImageAsync(
            restaurantId,
            menuItemId,
            key,
            CancellationToken.None);
    }

    [Fact]
    public async Task ImageProcessingBackgroundService_FindsUnprocessedItemsAndInvokesService()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();

        Guid restaurantId = Guid.NewGuid();
        Guid menuItemId = Guid.NewGuid();
        string key = $"restaurants/{restaurantId}/menu-items/{menuItemId}/dish.png";

        MenuItem item = new()
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = "Chiles en Nogada",
            BasePrice = 220.0m,
            ImageUrl = $"https://cdn.orderly.com/{key}",
            ThumbnailUrl = null,
            ImageUploadStatus = ImageUploadStatus.Confirmed
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IImageProcessingService processingService = Substitute.For<IImageProcessingService>();

        IServiceScope scope = Substitute.For<IServiceScope>();
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(CatalogDbContext)).Returns(dbContext);
        serviceProvider.GetService(typeof(IImageProcessingService)).Returns(processingService);
        scope.ServiceProvider.Returns(serviceProvider);

        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        ImageProcessingBackgroundService backgroundService = new(
            scopeFactory,
            NullLogger<ImageProcessingBackgroundService>.Instance);

        await backgroundService.ProcessPendingThumbnailsAsync(CancellationToken.None);

        await processingService.Received(1).ProcessMenuItemImageAsync(
            restaurantId,
            menuItemId,
            key,
            Arg.Any<CancellationToken>());
    }
}
