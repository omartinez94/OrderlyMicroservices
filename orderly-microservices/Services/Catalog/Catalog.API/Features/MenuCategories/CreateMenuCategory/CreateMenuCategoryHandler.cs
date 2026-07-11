namespace Catalog.API.Features.MenuCategories.CreateMenuCategory;

public record CreateMenuCategoryCommand(
    Guid RestaurantId,
    string Name,
    string Description,
    int DisplayOrder) : ICommand<CreateMenuCategoryResult>;

public record CreateMenuCategoryResult(int Id);

public class CreateMenuCategoryCommandValidator : AbstractValidator<CreateMenuCategoryCommand>
{
    public CreateMenuCategoryCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
    }
}

internal class CreateMenuCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<CreateMenuCategoryCommand, CreateMenuCategoryResult>
{
    public async Task<CreateMenuCategoryResult> Handle(CreateMenuCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = new MenuCategory
        {
            RestaurantId = command.RestaurantId,
            Name = command.Name,
            Description = command.Description,
            DisplayOrder = command.DisplayOrder,
            IsDeleted = false
        };

        dbContext.MenuCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(command.RestaurantId, cancellationToken);

        return new CreateMenuCategoryResult(category.Id);
    }
}
