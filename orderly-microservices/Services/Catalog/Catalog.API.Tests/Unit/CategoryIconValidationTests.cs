using Catalog.API.Features.MenuCategories.CreateMenuCategory;
using Catalog.API.Features.MenuCategories.UpdateMenuCategory;
using Catalog.API.Features.MenuSubCategories.CreateMenuSubCategory;
using Catalog.API.Features.MenuSubCategories.UpdateMenuSubCategory;
using FluentValidation.TestHelper;

namespace Catalog.API.Tests.Unit;

public sealed class CategoryIconValidationTests
{
    [Fact]
    public void CreateMenuCategory_ValidIcon_PassesValidation()
    {
        CreateMenuCategoryCommandValidator validator = new();
        CreateMenuCategoryCommand command = new(
            RestaurantId: Guid.NewGuid(),
            Name: "Pizzas",
            Description: "Freshly baked pizzas",
            DisplayOrder: 1,
            Icon: "lucide:pizza");

        TestValidationResult<CreateMenuCategoryCommand> result = validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Icon);
    }

    [Fact]
    public void CreateMenuCategory_EmojiIcon_PassesValidation()
    {
        CreateMenuCategoryCommandValidator validator = new();
        CreateMenuCategoryCommand command = new(
            RestaurantId: Guid.NewGuid(),
            Name: "Pizzas",
            Description: "Freshly baked pizzas",
            DisplayOrder: 1,
            Icon: "emoji:🍕");

        TestValidationResult<CreateMenuCategoryCommand> result = validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Icon);
    }

    [Fact]
    public void CreateMenuCategory_IconExceeding64Chars_FailsValidation()
    {
        CreateMenuCategoryCommandValidator validator = new();
        CreateMenuCategoryCommand command = new(
            RestaurantId: Guid.NewGuid(),
            Name: "Pizzas",
            Description: "Freshly baked pizzas",
            DisplayOrder: 1,
            Icon: new string('a', 65));

        TestValidationResult<CreateMenuCategoryCommand> result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Icon)
            .WithErrorMessage("Icon must not exceed 64 characters");
    }

    [Fact]
    public void UpdateMenuCategory_ValidIcon_PassesValidation()
    {
        UpdateMenuCategoryCommandValidator validator = new();
        UpdateMenuCategoryCommand command = new(
            Id: 10,
            Name: "Drinks",
            Description: "Cold drinks",
            DisplayOrder: 2,
            Icon: "lucide:cup-soda");

        TestValidationResult<UpdateMenuCategoryCommand> result = validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Icon);
    }

    [Fact]
    public void CreateMenuSubCategory_ValidIcon_PassesValidation()
    {
        CreateMenuSubCategoryCommandValidator validator = new();
        CreateMenuSubCategoryCommand command = new(
            CategoryId: 1,
            Name: "Classic Pizzas",
            Description: "Old style",
            DisplayOrder: 1,
            IsActive: true,
            Icon: "emoji:🍕");

        TestValidationResult<CreateMenuSubCategoryCommand> result = validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Icon);
    }

    [Fact]
    public void UpdateMenuSubCategory_IconExceeding64Chars_FailsValidation()
    {
        UpdateMenuSubCategoryCommandValidator validator = new();
        UpdateMenuSubCategoryCommand command = new(
            Id: 5,
            CategoryId: 1,
            Name: "Classic Pizzas",
            Description: "Old style",
            DisplayOrder: 1,
            IsActive: true,
            Icon: new string('x', 65));

        TestValidationResult<UpdateMenuSubCategoryCommand> result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Icon)
            .WithErrorMessage("Icon must not exceed 64 characters");
    }
}
