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
    IOutboxPublisher outbox,
    IFeatureManager featureManager) : ICommandHandler<RemoveMenuItemIngredientCommand, RemoveMenuItemIngredientResult>
{
    public async Task<RemoveMenuItemIngredientResult> Handle(RemoveMenuItemIngredientCommand command, CancellationToken cancellationToken)
    {
        var link = await dbContext.MenuItemIngredients
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.MenuItemId == command.MenuItemId, cancellationToken);

        if (link is null)
        {
            throw new NotFoundException(nameof(MenuItemIngredient), command.Id);
        }

        var menuRestaurantId = await dbContext.MenuItems
            .Where(m => m.Id == command.MenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        var ingredientRestaurantId = await dbContext.Ingredients
            .Where(i => i.Id == link.IngredientId)
            .Select(i => (Guid?)i.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.MenuItemIngredients.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (menuRestaurantId is { } menuRid)
        {
            await cache.InvalidateMenuAsync(menuRid, cancellationToken);
        }
        if (ingredientRestaurantId is { } ingredientRid)
        {
            await cache.InvalidateIngredientsAsync(ingredientRid, cancellationToken);
        }

        if (menuRestaurantId is { } menuRid2 &&
            await featureManager.IsEnabledAsync("CatalogMenuEvents", cancellationToken).ConfigureAwait(false))
        {
            await outbox.PublishAsync(new MenuItemChangedIntegrationEvent
            {
                MenuItemId = command.MenuItemId,
                RestaurantId = menuRid2,
                ChangeType = MenuItemChangeType.Updated,
            }, cancellationToken).ConfigureAwait(false);
        }

        return new RemoveMenuItemIngredientResult(true);
    }
}
