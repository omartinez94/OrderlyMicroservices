using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Exceptions;
using Catalog.API.Application.Abstractions;
using Catalog.API.Features.Menu.GetMenu;
using Catalog.API.Features.MenuItems.Common;
using Catalog.API.Features.MenuItems.CreateMenuItem;
using Catalog.API.Features.MenuItems.DeleteMenuItem;
using Catalog.API.Features.MenuItems.PatchMenuItemAvailability;
using Catalog.API.Features.MenuItems.UpdateMenuItem;
using Catalog.API.Features.PriceHistories.CreatePriceHistory;
using Catalog.API.Readers;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Catalog.API.Tests.Unit;

public sealed class MenuManagementPhase1Tests
{
    private static CatalogDbContext CreateInMemoryDbContext()
    {
        ServiceCollection services = new();
        services.AddHttpContextAccessor();
        ServiceProvider appServiceProvider = services.BuildServiceProvider();

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: $"catalog-unit-{Guid.NewGuid()}")
            .AddInterceptors(new AuditableEntityInterceptor())
            .ConfigureWarnings(w => w.Ignore(
                InMemoryEventId.TransactionIgnoredWarning,
                CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseApplicationServiceProvider(appServiceProvider)
            .Options;

        return new CatalogDbContext(options);
    }

    // ----------------------------------------------------------------
    // GetMenuQueryHandler
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetMenuHandler_WhenReaderReturnsSnapshot_ReturnsSnapshotWithoutQueryingRestaurants()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        IMenuReader menuReader = Substitute.For<IMenuReader>();
        Guid restaurantId = Guid.NewGuid();
        MenuSnapshot expectedSnapshot = new(
            RestaurantId: restaurantId,
            SnapshotAt: NodaTime.SystemClock.Instance.GetCurrentInstant(),
            Categories:
            [
                new MenuCategoryNode(
                    Id: 1,
                    Name: "Mains",
                    Description: "Main courses",
                    DisplayOrder: 1,
                    SubCategories: [])
            ]);

        menuReader.GetByRestaurantAsync(restaurantId, Arg.Any<CancellationToken>())
            .Returns(expectedSnapshot);

        GetMenuQueryHandler sut = new(menuReader, dbContext);

        GetMenuResult result = await sut.Handle(new GetMenuQuery(restaurantId), CancellationToken.None);

        result.Snapshot.Should().BeSameAs(expectedSnapshot);
        result.Snapshot.Categories.Should().ContainSingle();
    }

    [Fact]
    public async Task GetMenuHandler_WhenReaderReturnsNullAndRestaurantExists_ReturnsEmptyMenuSnapshot()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        dbContext.Restaurants.Add(new Restaurant
        {
            Id = restaurantId,
            BrandId = Guid.NewGuid(),
            Name = "Empty Menu Bistro",
            Address = "100 Main St",
            TaxRate = 0.16m
        });
        await dbContext.SaveChangesAsync();

        IMenuReader menuReader = Substitute.For<IMenuReader>();
        menuReader.GetByRestaurantAsync(restaurantId, Arg.Any<CancellationToken>())
            .Returns((MenuSnapshot?)null);

        GetMenuQueryHandler sut = new(menuReader, dbContext);

        GetMenuResult result = await sut.Handle(new GetMenuQuery(restaurantId), CancellationToken.None);

        result.Snapshot.RestaurantId.Should().Be(restaurantId);
        result.Snapshot.Categories.Should().BeEmpty();
        result.Snapshot.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task GetMenuHandler_WhenReaderReturnsNullAndRestaurantDoesNotExist_ThrowsNotFoundException()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid missingRestaurantId = Guid.NewGuid();
        IMenuReader menuReader = Substitute.For<IMenuReader>();
        menuReader.GetByRestaurantAsync(missingRestaurantId, Arg.Any<CancellationToken>())
            .Returns((MenuSnapshot?)null);

        GetMenuQueryHandler sut = new(menuReader, dbContext);

        Func<Task> act = () => sut.Handle(new GetMenuQuery(missingRestaurantId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*{missingRestaurantId}*");
    }

    [Fact]
    public async Task MenuReader_IncludesDefaultCourseNumberInSnapshot()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();

        MenuCategory category = new()
        {
            Id = 10,
            RestaurantId = restaurantId,
            Name = "Starters",
            Description = "Appetizers",
            DisplayOrder = 1
        };
        MenuSubCategory subCategory = new()
        {
            Id = 20,
            CategoryId = 10,
            Name = "Cold",
            Description = "Cold starters",
            DisplayOrder = 1,
            IsActive = true
        };
        MenuItem item = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            SubCategoryId = 20,
            Name = "Ceviche",
            Description = "Fresh fish",
            BasePrice = 180m,
            ImageUrl = "https://cdn.local/ceviche.jpg",
            AvailabilityStatus = AvailabilityStatus.Available,
            IsAvailable = true,
            DefaultCourseNumber = 2
        };

        dbContext.MenuCategories.Add(category);
        dbContext.MenuSubCategories.Add(subCategory);
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        MenuReader sut = new(dbContext);

        MenuSnapshot? snapshot = await sut.GetByRestaurantAsync(restaurantId, CancellationToken.None);

        snapshot.Should().NotBeNull();
        MenuItemNode node = snapshot!.Categories.Single().SubCategories.Single().Items.Single();
        node.DefaultCourseNumber.Should().Be(2);
        node.ImageUrl.Should().Be("https://cdn.local/ceviche.jpg");
    }

    // ----------------------------------------------------------------
    // PatchMenuItemAvailabilityValidator
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("Available")]
    [InlineData("available")]
    [InlineData("Limited")]
    [InlineData("LIMITED")]
    [InlineData("Unavailable")]
    [InlineData("unavailable")]
    public void PatchAvailabilityValidator_ValidStatus_Passes(string status)
    {
        PatchMenuItemAvailabilityValidator validator = new();
        PatchMenuItemAvailabilityCommand command = new(Guid.NewGuid(), Guid.NewGuid(), status);

        TestValidationResult<PatchMenuItemAvailabilityCommand> result = validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("OutOfStock")]
    [InlineData("SoldOut")]
    [InlineData("0")]
    [InlineData("99")]
    public void PatchAvailabilityValidator_InvalidStatus_Fails(string status)
    {
        PatchMenuItemAvailabilityValidator validator = new();
        PatchMenuItemAvailabilityCommand command = new(Guid.NewGuid(), Guid.NewGuid(), status);

        TestValidationResult<PatchMenuItemAvailabilityCommand> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AvailabilityStatus);
    }

    // ----------------------------------------------------------------
    // PatchMenuItemAvailabilityHandler
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("Available", AvailabilityStatus.Available, true)]
    [InlineData("Limited", AvailabilityStatus.Limited, true)]
    [InlineData("Unavailable", AvailabilityStatus.Unavailable, false)]
    public async Task PatchAvailabilityHandler_EnforcesAvailabilityInvariantAndNotifies(
        string requestedStatus,
        AvailabilityStatus expectedEnum,
        bool expectedIsAvailable)
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        MenuItem item = new()
        {
            Id = itemId,
            RestaurantId = restaurantId,
            Name = "Tacos",
            BasePrice = 95m,
            AvailabilityStatus = AvailabilityStatus.Available,
            IsAvailable = true
        };
        dbContext.MenuItems.Add(item);
        await dbContext.SaveChangesAsync();

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);
        currentUser.Roles.Returns(new[] { "Manager" });

        PatchMenuItemAvailabilityHandler sut = new(dbContext, notifier, currentUser);

        PatchMenuItemAvailabilityResult result = await sut.Handle(
            new PatchMenuItemAvailabilityCommand(restaurantId, itemId, requestedStatus),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsAvailable.Should().Be(expectedIsAvailable);
        result.AvailabilityStatus.Should().Be(expectedEnum.ToString());

        item.AvailabilityStatus.Should().Be(expectedEnum);
        item.IsAvailable.Should().Be(expectedIsAvailable);

        await notifier.Received(1).SaveAndNotifyAsync(
            item,
            MenuItemChangeType.Updated,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAvailabilityHandler_WhenItemBelongsToAnotherRestaurant_ThrowsNotFoundException()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid ownerRestaurantId = Guid.NewGuid();
        Guid routeRestaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        dbContext.MenuItems.Add(new MenuItem
        {
            Id = itemId,
            RestaurantId = ownerRestaurantId,
            Name = "Enchiladas",
            BasePrice = 120m
        });
        await dbContext.SaveChangesAsync();

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(routeRestaurantId);
        currentUser.Roles.Returns(new[] { "Manager" });

        PatchMenuItemAvailabilityHandler sut = new(dbContext, notifier, currentUser);

        Func<Task> act = () => sut.Handle(
            new PatchMenuItemAvailabilityCommand(routeRestaurantId, itemId, "Unavailable"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await notifier.DidNotReceiveWithAnyArgs().SaveAndNotifyAsync(default!, default, default);
    }

    [Fact]
    public async Task PatchAvailabilityHandler_WhenCallerRestaurantClaimDoesNotMatchRoute_ThrowsForbiddenException()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid routeRestaurantId = Guid.NewGuid();
        Guid callerClaimRestaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        dbContext.MenuItems.Add(new MenuItem
        {
            Id = itemId,
            RestaurantId = routeRestaurantId,
            Name = "Pozole",
            BasePrice = 150m
        });
        await dbContext.SaveChangesAsync();

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(callerClaimRestaurantId);
        currentUser.Roles.Returns(new[] { "KitchenManager" });

        PatchMenuItemAvailabilityHandler sut = new(dbContext, notifier, currentUser);

        Func<Task> act = () => sut.Handle(
            new PatchMenuItemAvailabilityCommand(routeRestaurantId, itemId, "Unavailable"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await notifier.DidNotReceiveWithAnyArgs().SaveAndNotifyAsync(default!, default, default);
    }

    [Fact]
    public async Task PatchAvailabilityHandler_WhenSuperAdminHasDifferentRestaurantClaim_AllowsMutation()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid routeRestaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        dbContext.MenuItems.Add(new MenuItem
        {
            Id = itemId,
            RestaurantId = routeRestaurantId,
            Name = "Tamal",
            BasePrice = 50m,
            AvailabilityStatus = AvailabilityStatus.Available,
            IsAvailable = true
        });
        await dbContext.SaveChangesAsync();

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new[] { "SuperAdmin" });

        PatchMenuItemAvailabilityHandler sut = new(dbContext, notifier, currentUser);

        PatchMenuItemAvailabilityResult result = await sut.Handle(
            new PatchMenuItemAvailabilityCommand(routeRestaurantId, itemId, "Unavailable"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsAvailable.Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // MenuItemMutationNotifier
    // ----------------------------------------------------------------

    [Fact]
    public async Task MenuItemMutationNotifier_SaveAndNotifyAsync_StagesOutboxWithImageAndCourse_SavesDb_ThenInvalidatesCache()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        MenuItem item = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = "Ribeye",
            BasePrice = 450m,
            IsAvailable = true,
            AvailabilityStatus = AvailabilityStatus.Available,
            ImageUrl = "https://cdn.local/ribeye.webp",
            DefaultCourseNumber = 3
        };
        dbContext.MenuItems.Add(item);

        IOutboxPublisher outbox = Substitute.For<IOutboxPublisher>();
        ICatalogCache cache = Substitute.For<ICatalogCache>();
        IFeatureManager featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("CatalogMenuEvents", Arg.Any<CancellationToken>())
            .Returns(true);

        List<string> callOrder = [];
        outbox.When(x => x.PublishAsync(Arg.Any<MenuItemChangedIntegrationEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("outbox"));
        cache.When(x => x.InvalidateMenuAsync(restaurantId, Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                // By the time cache invalidation runs, the entity state must already be Unchanged (committed).
                dbContext.Entry(item).State.Should().Be(EntityState.Unchanged);
                callOrder.Add("cache");
            });

        MenuItemMutationNotifier sut = new(dbContext, outbox, cache, featureManager);

        await sut.SaveAndNotifyAsync(item, MenuItemChangeType.Updated, CancellationToken.None);

        callOrder.Should().Equal("outbox", "cache");
        await outbox.Received(1).PublishAsync(
            Arg.Is<MenuItemChangedIntegrationEvent>(e =>
                e.MenuItemId == item.Id &&
                e.RestaurantId == restaurantId &&
                e.ChangeType == MenuItemChangeType.Updated &&
                e.Name == "Ribeye" &&
                e.BasePrice == 450m &&
                e.IsAvailable == true &&
                e.AvailabilityStatus == "Available" &&
                e.ImageUrl == "https://cdn.local/ribeye.webp" &&
                e.DefaultCourseNumber == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MenuItemMutationNotifier_OnDelete_ClearsOptionalFieldsIncludingImageAndCourse()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        MenuItem item = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = "Deleted Dish",
            BasePrice = 100m,
            IsAvailable = false,
            AvailabilityStatus = AvailabilityStatus.Unavailable,
            ImageUrl = "https://cdn.local/old.png",
            DefaultCourseNumber = 1,
            IsDeleted = true
        };
        dbContext.MenuItems.Add(item);

        IOutboxPublisher outbox = Substitute.For<IOutboxPublisher>();
        ICatalogCache cache = Substitute.For<ICatalogCache>();
        IFeatureManager featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("CatalogMenuEvents", Arg.Any<CancellationToken>())
            .Returns(true);

        MenuItemMutationNotifier sut = new(dbContext, outbox, cache, featureManager);

        await sut.SaveAndNotifyAsync(item, MenuItemChangeType.Deleted, CancellationToken.None);

        await outbox.Received(1).PublishAsync(
            Arg.Is<MenuItemChangedIntegrationEvent>(e =>
                e.MenuItemId == item.Id &&
                e.RestaurantId == restaurantId &&
                e.ChangeType == MenuItemChangeType.Deleted &&
                e.Name == null &&
                e.BasePrice == null &&
                e.IsAvailable == null &&
                e.AvailabilityStatus == null &&
                e.ImageUrl == null &&
                e.DefaultCourseNumber == null),
            Arg.Any<CancellationToken>());
        await cache.Received(1).InvalidateMenuAsync(restaurantId, Arg.Any<CancellationToken>());
    }

    // ----------------------------------------------------------------
    // Create / Update / Delete MenuItem Handlers & Validators
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateMenuItemHandler_PreservesExistingImageUrl_UpdatesDefaultCourseNumber_AndDerivesIsAvailable()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        const string originalImageUrl = "https://blob.local/menu-assets/original.webp";

        MenuItem existing = new()
        {
            Id = itemId,
            RestaurantId = restaurantId,
            Name = "Burger",
            Description = "Classic",
            BasePrice = 120m,
            ImageUrl = originalImageUrl,
            AvailabilityStatus = AvailabilityStatus.Available,
            IsAvailable = true,
            DefaultCourseNumber = 1
        };
        dbContext.MenuItems.Add(existing);
        await dbContext.SaveChangesAsync();

        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();
        IPriceHistoryRecorder priceHistory = Substitute.For<IPriceHistoryRecorder>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();

        UpdateMenuItemCommandHandler sut = new(dbContext, notifier, priceHistory, currentUser);

        UpdateMenuItemCommand command = new()
        {
            Id = itemId,
            Name = "Smash Burger",
            Description = "Double patty",
            BasePrice = 145m,
            ImageUrl = "https://evil.local/overwrite.png",
            AvailabilityStatus = AvailabilityStatus.Unavailable,
            IsAvailable = true, // Contradictory input — handler must derive false from Unavailable
            DefaultCourseNumber = 2
        };

        UpdateMenuItemResult result = await sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        existing.ImageUrl.Should().Be(originalImageUrl, "PUT must never overwrite ImageUrl");
        existing.DefaultCourseNumber.Should().Be(2);
        existing.AvailabilityStatus.Should().Be(AvailabilityStatus.Unavailable);
        existing.IsAvailable.Should().BeFalse("Unavailable status must derive IsAvailable = false");
        await notifier.Received(1).SaveAndNotifyAsync(existing, MenuItemChangeType.Updated, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(-1, false)]
    public void CreateAndUpdateValidators_EnforceDefaultCourseNumberRange(int courseNumber, bool expectedValid)
    {
        CreateMenuItemCommandValidator createValidator = new();
        UpdateMenuItemCommandValidator updateValidator = new();

        CreateMenuItemCommand createCmd = new()
        {
            RestaurantId = Guid.NewGuid(),
            Name = "Dish",
            BasePrice = 50m,
            DefaultCourseNumber = courseNumber
        };
        UpdateMenuItemCommand updateCmd = new()
        {
            Id = Guid.NewGuid(),
            Name = "Dish",
            BasePrice = 50m,
            DefaultCourseNumber = courseNumber
        };

        TestValidationResult<CreateMenuItemCommand> createResult = createValidator.TestValidate(createCmd);
        TestValidationResult<UpdateMenuItemCommand> updateResult = updateValidator.TestValidate(updateCmd);

        if (expectedValid)
        {
            createResult.ShouldNotHaveValidationErrorFor(x => x.DefaultCourseNumber);
            updateResult.ShouldNotHaveValidationErrorFor(x => x.DefaultCourseNumber);
        }
        else
        {
            createResult.ShouldHaveValidationErrorFor(x => x.DefaultCourseNumber);
            updateResult.ShouldHaveValidationErrorFor(x => x.DefaultCourseNumber);
        }
    }

    [Fact]
    public async Task CreateAndDeleteMenuItemHandlers_DelegateToMutationNotifier()
    {
        await using CatalogDbContext dbContext = CreateInMemoryDbContext();
        Guid restaurantId = Guid.NewGuid();
        IMenuItemMutationNotifier notifier = Substitute.For<IMenuItemMutationNotifier>();

        CreateMenuItemCommandHandler createHandler = new(dbContext, notifier);
        CreateMenuItemResult created = await createHandler.Handle(new CreateMenuItemCommand
        {
            RestaurantId = restaurantId,
            Name = "Flan",
            BasePrice = 65m,
            AvailabilityStatus = AvailabilityStatus.Limited,
            IsAvailable = false, // Contradictory input — Limited must derive IsAvailable = true
            DefaultCourseNumber = 3
        }, CancellationToken.None);

        MenuItem? persisted = dbContext.MenuItems.Local.FirstOrDefault(m => m.Id == created.Id);
        persisted.Should().NotBeNull();
        persisted!.IsAvailable.Should().BeTrue();
        persisted.DefaultCourseNumber.Should().Be(3);
        persisted.ImageUrl.Should().BeEmpty();
        await notifier.Received(1).SaveAndNotifyAsync(persisted, MenuItemChangeType.Created, Arg.Any<CancellationToken>());

        await dbContext.SaveChangesAsync();

        DeleteMenuItemCommandHandler deleteHandler = new(dbContext, notifier);
        DeleteMenuItemResult deleted = await deleteHandler.Handle(new DeleteMenuItemCommand(created.Id), CancellationToken.None);

        deleted.Success.Should().BeTrue();
        persisted.IsDeleted.Should().BeTrue();
        persisted.IsAvailable.Should().BeFalse();
        await notifier.Received(1).SaveAndNotifyAsync(persisted, MenuItemChangeType.Deleted, Arg.Any<CancellationToken>());
    }
}
