using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.DeleteMenuCategory;

public record DeleteMenuCategoryCommand(int Id) : ICommand<DeleteMenuCategoryResult>;

public record DeleteMenuCategoryResult(bool IsSuccess);

public class DeleteMenuCategoryCommandValidator : AbstractValidator<DeleteMenuCategoryCommand>
{
    public DeleteMenuCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class DeleteMenuCategoryCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteMenuCategoryCommand, DeleteMenuCategoryResult>
{
    public async Task<DeleteMenuCategoryResult> Handle(DeleteMenuCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await dbContext.MenuCategories
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (category == null)
        {
            throw new MenuCategoryNotFoundException(command.Id);
        }

        category.IsDeleted = true;
        category.DeletedAt = SystemClock.Instance.GetCurrentInstant();

        dbContext.MenuCategories.Update(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteMenuCategoryResult(true);
    }
}
