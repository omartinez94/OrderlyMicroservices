using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemIngredients.RemoveMenuItemIngredient;

public record RemoveMenuItemIngredientCommand(Guid MenuItemId, int Id) : ICommand<RemoveMenuItemIngredientResult>;

public record RemoveMenuItemIngredientResult(bool Success);

public class RemoveMenuItemIngredientCommandValidator : AbstractValidator<RemoveMenuItemIngredientCommand>
{
    public RemoveMenuItemIngredientCommandValidator()
    {
        RuleFor(x => x.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required");
    }
}

internal class RemoveMenuItemIngredientCommandHandler(CatalogDbContext dbContext) : ICommandHandler<RemoveMenuItemIngredientCommand, RemoveMenuItemIngredientResult>
{
    public async Task<RemoveMenuItemIngredientResult> Handle(RemoveMenuItemIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.MenuItemIngredients
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.MenuItemId == command.MenuItemId, cancellationToken);

        if (ingredient is null)
        {
            throw new NotFoundException(nameof(MenuItemIngredient), command.Id);
        }

        dbContext.MenuItemIngredients.Remove(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RemoveMenuItemIngredientResult(true);
    }
}
