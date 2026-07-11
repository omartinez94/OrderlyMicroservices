using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.UpdateMenuCategory;

public record UpdateMenuCategoryCommand(
    int Id,
    string Name,
    string Description,
    int DisplayOrder) : ICommand<UpdateMenuCategoryResult>;

public record UpdateMenuCategoryResult(bool IsSuccess);

public class UpdateMenuCategoryCommandValidator : AbstractValidator<UpdateMenuCategoryCommand>
{
    public UpdateMenuCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
    }
}

internal class UpdateMenuCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<UpdateMenuCategoryCommand, UpdateMenuCategoryResult>
{
    public async Task<UpdateMenuCategoryResult> Handle(UpdateMenuCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await dbContext.MenuCategories
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (category == null)
        {
            throw new MenuCategoryNotFoundException(command.Id);
        }

        category.Name = command.Name;
        category.Description = command.Description;
        category.DisplayOrder = command.DisplayOrder;

        dbContext.MenuCategories.Update(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(category.RestaurantId, cancellationToken);

        return new UpdateMenuCategoryResult(true);
    }
}
