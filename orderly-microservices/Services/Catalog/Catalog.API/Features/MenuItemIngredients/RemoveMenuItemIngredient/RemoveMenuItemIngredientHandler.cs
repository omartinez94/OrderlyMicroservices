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

internal class RemoveMenuItemIngredientCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<RemoveMenuItemIngredientCommand, RemoveMenuItemIngredientResult>
{
    public async Task<RemoveMenuItemIngredientResult> Handle(RemoveMenuItemIngredientCommand command, CancellationToken cancellationToken)
    {
        MenuItemIngredient? link = await dbContext.MenuItemIngredients
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.MenuItemId == command.MenuItemId, cancellationToken);

        if (link is null)
        {
            throw new NotFoundException(nameof(MenuItemIngredient), command.Id);
        }

        Guid menuRestaurantId = await dbContext.MenuItems
            .Where(m => m.Id == command.MenuItemId && !m.IsDeleted)
            .Select(m => m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (menuRestaurantId != Guid.Empty)
        {
            currentUser.EnsureTenantAccess(menuRestaurantId);
        }

        Guid? ingredientRestaurantId = await dbContext.Ingredients
            .Where(i => i.Id == link.IngredientId)
            .Select(i => (Guid?)i.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.MenuItemIngredients.Remove(link);

        // Domain event BEFORE SaveChanges.
        link.AddDomainEvent(new MenuItemIngredientChangedDomainEvent(
            link.Id,
            command.MenuItemId,
            link.IngredientId,
            MenuItemIngredientChangedDomainEvent.ChangeKind.Deleted));

        await notifier.SaveAndNotifyRelatedAsync(command.MenuItemId, menuRestaurantId, cancellationToken).ConfigureAwait(false);

        if (ingredientRestaurantId is { } ingredientRid)
        {
            await cache.InvalidateIngredientsAsync(ingredientRid, cancellationToken);
        }

        return new RemoveMenuItemIngredientResult(true);
    }
}
