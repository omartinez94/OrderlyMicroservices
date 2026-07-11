using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuSubCategories.DeleteMenuSubCategory;

/// <summary>
/// Command soft-deleting a <see cref="MenuSubCategory"/> — sets
/// <see cref="MenuSubCategory.IsDeleted"/> = true and stamps
/// <see cref="MenuSubCategory.DeletedAt"/>. Reads in
/// <c>GetMenuSubCategories</c> / <c>GetMenuSubCategoryById</c> already filter
/// by <c>!IsDeleted</c>, so the row stops appearing without losing history.
/// </summary>
/// <param name="Id">Primary key of the sub-category to soft-delete.</param>
public record DeleteMenuSubCategoryCommand(int Id) : ICommand<DeleteMenuSubCategoryResult>;

public record DeleteMenuSubCategoryResult(bool Success);

public class DeleteMenuSubCategoryCommandValidator : AbstractValidator<DeleteMenuSubCategoryCommand>
{
    public DeleteMenuSubCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class DeleteMenuSubCategoryCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<DeleteMenuSubCategoryCommand, DeleteMenuSubCategoryResult>
{
    public async Task<DeleteMenuSubCategoryResult> Handle(DeleteMenuSubCategoryCommand command, CancellationToken cancellationToken)
    {
        var subCategory = await dbContext.MenuSubCategories
            .FirstOrDefaultAsync(sc => sc.Id == command.Id, cancellationToken);

        if (subCategory is null)
        {
            throw new MenuSubCategoryNotFoundException(command.Id);
        }

        // Idempotent: a second delete on an already-deleted row is a no-op
        // (returns Success=true without re-stamping DeletedAt). The
        // exception type is still raised when the row doesn't exist at all.
        if (!subCategory.IsDeleted)
        {
            subCategory.IsDeleted = true;
            subCategory.DeletedAt = SystemClock.Instance.GetCurrentInstant();

            await dbContext.SaveChangesAsync(cancellationToken);

            var restaurantId = await dbContext.MenuCategories
                .Where(c => c.Id == subCategory.CategoryId)
                .Select(c => (Guid?)c.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken);

            if (restaurantId is not null)
            {
                await cache.InvalidateMenuAsync(restaurantId.Value, cancellationToken);
            }
        }

        return new DeleteMenuSubCategoryResult(true);
    }
}