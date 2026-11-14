using BuildingBlocks.Enums;
using Catalog.API.Features.MenuItems.CreateMenuItem;
using Catalog.API.Features.MenuItems.UpdateMenuItem;
using Catalog.API.Models;
using FluentValidation.TestHelper;
using Xunit;

namespace Catalog.API.Tests.Unit;

public class ImageStoragePhase2Tests
{
    private readonly CreateMenuItemCommandValidator _createValidator = new();
    private readonly UpdateMenuItemCommandValidator _updateValidator = new();

    [Fact]
    public void CreateMenuItemCommandValidator_WithInvalidImageUrl_ShouldHaveValidationError()
    {
        CreateMenuItemCommand command = new()
        {
            RestaurantId = Guid.NewGuid(),
            Name = "Tacos al Pastor",
            BasePrice = 95.0m,
            ImageUrl = "not-a-valid-url"
        };

        TestValidationResult<CreateMenuItemCommand> result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("ImageUrl must be a valid absolute URL when provided");
    }

    [Fact]
    public void CreateMenuItemCommandValidator_WithValidImageUrl_ShouldPassValidation()
    {
        CreateMenuItemCommand command = new()
        {
            RestaurantId = Guid.NewGuid(),
            Name = "Tacos al Pastor",
            BasePrice = 95.0m,
            ImageUrl = "https://cdn.orderly.com/restaurants/123/menu-items/tacos.webp"
        };

        TestValidationResult<CreateMenuItemCommand> result = _createValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void CreateMenuItemCommandValidator_WithEmptyImageUrl_ShouldPassValidation()
    {
        CreateMenuItemCommand command = new()
        {
            RestaurantId = Guid.NewGuid(),
            Name = "Tacos al Pastor",
            BasePrice = 95.0m,
            ImageUrl = string.Empty
        };

        TestValidationResult<CreateMenuItemCommand> result = _createValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void UpdateMenuItemCommandValidator_WithInvalidImageUrl_ShouldHaveValidationError()
    {
        UpdateMenuItemCommand command = new()
        {
            Id = Guid.NewGuid(),
            Name = "Tacos al Pastor",
            BasePrice = 95.0m,
            ImageUrl = "ftp://invalid-or-plain-text"
        };

        // Note: ftp is an absolute URI, let's test a non-URI string
        command.ImageUrl = "just-plain-text";
        TestValidationResult<UpdateMenuItemCommand> result = _updateValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("ImageUrl must be a valid absolute URL when provided");
    }

    [Fact]
    public void MenuItemModel_HoldsNewImageFields_WithCorrectDefaults()
    {
        MenuItem item = new()
        {
            Id = Guid.NewGuid(),
            Name = "Guacamole",
            BasePrice = 120.0m
        };

        Assert.Equal(ImageUploadStatus.None, item.ImageUploadStatus);
        Assert.Null(item.ThumbnailUrl);
        Assert.Null(item.PendingImageKey);

        item.ThumbnailUrl = "https://cdn.orderly.com/thumb.webp";
        item.PendingImageKey = "staging/key.jpg";
        item.ImageUploadStatus = ImageUploadStatus.Pending;

        Assert.Equal("https://cdn.orderly.com/thumb.webp", item.ThumbnailUrl);
        Assert.Equal("staging/key.jpg", item.PendingImageKey);
        Assert.Equal(ImageUploadStatus.Pending, item.ImageUploadStatus);
    }

    [Fact]
    public void RestaurantModel_HoldsLogoAndBannerUrls()
    {
        Restaurant restaurant = new()
        {
            Id = Guid.NewGuid(),
            Name = "La Taqueria",
            LogoUrl = "https://cdn.orderly.com/logo.webp",
            BannerUrl = "https://cdn.orderly.com/banner.webp"
        };

        Assert.Equal("https://cdn.orderly.com/logo.webp", restaurant.LogoUrl);
        Assert.Equal("https://cdn.orderly.com/banner.webp", restaurant.BannerUrl);
    }
}
