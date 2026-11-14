using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.UpdateMenuCategory;

public record UpdateMenuCategoryCommand(
    int Id,
    string Name,
    string Description,
    int DisplayOrder,
    string? Icon = null) : ICommand<UpdateMenuCategoryResult>;

public record UpdateMenuCategoryResult(bool IsSuccess);

public class UpdateMenuCategoryCommandValidator : AbstractValidator<UpdateMenuCategoryCommand>
{
    public UpdateMenuCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.Icon).MaximumLength(64).WithMessage("Icon must not exceed 64 characters");
    }
}

internal class UpdateMenuCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    ICurrentUser? currentUser = null) : ICommandHandler<UpdateMenuCategoryCommand, UpdateMenuCategoryResult>
{
    public async Task<UpdateMenuCategoryResult> Handle(UpdateMenuCategoryCommand command, CancellationToken cancellationToken)
    {
        MenuCategory? category = await dbContext.MenuCategories
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (category is null)
        {
            throw new MenuCategoryNotFoundException(command.Id);
        }

        currentUser.EnsureTenantAccess(category.RestaurantId);

        category.Name = command.Name;
        category.Description = command.Description;
        category.DisplayOrder = command.DisplayOrder;
        category.Icon = command.Icon;

        dbContext.MenuCategories.Update(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(category.RestaurantId, cancellationToken);

        return new UpdateMenuCategoryResult(true);
    }
}
