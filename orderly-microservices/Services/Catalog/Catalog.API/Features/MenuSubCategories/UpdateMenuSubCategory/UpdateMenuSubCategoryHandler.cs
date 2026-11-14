using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuSubCategories.UpdateMenuSubCategory;

public record UpdateMenuSubCategoryCommand(
    int Id,
    int CategoryId,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive,
    string? Icon = null) : ICommand<UpdateMenuSubCategoryResult>;

public record UpdateMenuSubCategoryResult(bool Success);

public class UpdateMenuSubCategoryCommandValidator : AbstractValidator<UpdateMenuSubCategoryCommand>
{
    public UpdateMenuSubCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("CategoryId must be greater than 0");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.Icon)
            .MaximumLength(64).WithMessage("Icon must not exceed 64 characters");
    }
}

internal class UpdateMenuSubCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    ICurrentUser? currentUser = null) : ICommandHandler<UpdateMenuSubCategoryCommand, UpdateMenuSubCategoryResult>
{
    public async Task<UpdateMenuSubCategoryResult> Handle(UpdateMenuSubCategoryCommand command, CancellationToken cancellationToken)
    {
        MenuSubCategory? subCategory = await dbContext.MenuSubCategories.FindAsync([command.Id], cancellationToken);
        if (subCategory is null)
        {
            throw new MenuSubCategoryNotFoundException(command.Id);
        }

        Guid newCategoryRestaurantId = (await dbContext.MenuCategories
            .Where(c => c.Id == command.CategoryId)
            .Select(c => (Guid?)c.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken))
            ?? throw new MenuCategoryNotFoundException(command.CategoryId);

        currentUser.EnsureTenantAccess(newCategoryRestaurantId);

        subCategory.CategoryId = command.CategoryId;
        subCategory.Name = command.Name;
        subCategory.Description = command.Description ?? string.Empty;
        subCategory.DisplayOrder = command.DisplayOrder;
        subCategory.IsActive = command.IsActive;
        subCategory.Icon = command.Icon;

        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(newCategoryRestaurantId, cancellationToken);

        return new UpdateMenuSubCategoryResult(true);
    }
}
