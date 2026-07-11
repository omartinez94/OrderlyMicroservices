using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuSubCategories.CreateMenuSubCategory;

public record CreateMenuSubCategoryCommand(
    int CategoryId,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive) : ICommand<CreateMenuSubCategoryResult>;

public record CreateMenuSubCategoryResult(int Id);

public class CreateMenuSubCategoryCommandValidator : AbstractValidator<CreateMenuSubCategoryCommand>
{
    public CreateMenuSubCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("CategoryId must be greater than 0");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255).WithMessage("Name must not exceed 255 characters");
    }
}

internal class CreateMenuSubCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<CreateMenuSubCategoryCommand, CreateMenuSubCategoryResult>
{
    public async Task<CreateMenuSubCategoryResult> Handle(CreateMenuSubCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await dbContext.MenuCategories
            .Where(c => c.Id == command.CategoryId)
            .Select(c => new { c.Id, c.RestaurantId })
            .FirstOrDefaultAsync(cancellationToken);
        if (category is null)
        {
            throw new MenuCategoryNotFoundException(command.CategoryId);
        }

        var subCategory = new MenuSubCategory
        {
            CategoryId = command.CategoryId,
            Name = command.Name,
            Description = command.Description ?? string.Empty,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            IsDeleted = false
        };

        dbContext.MenuSubCategories.Add(subCategory);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(category.RestaurantId, cancellationToken);

        return new CreateMenuSubCategoryResult(subCategory.Id);
    }
}
