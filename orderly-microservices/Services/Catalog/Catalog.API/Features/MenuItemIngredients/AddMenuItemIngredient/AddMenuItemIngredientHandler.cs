using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemIngredients.AddMenuItemIngredient;

public record AddMenuItemIngredientCommand(
    Guid MenuItemId,
    int IngredientId,
    decimal QuantityRequired,
    bool IsOptional) : ICommand<AddMenuItemIngredientResult>;

public record AddMenuItemIngredientResult(int Id);

public class AddMenuItemIngredientCommandValidator : AbstractValidator<AddMenuItemIngredientCommand>
{
    public AddMenuItemIngredientCommandValidator()
    {
        RuleFor(x => x.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
        RuleFor(x => x.IngredientId).GreaterThan(0).WithMessage("IngredientId is required");
        RuleFor(x => x.QuantityRequired).GreaterThan(0).WithMessage("QuantityRequired must be greater than 0");
    }
}

internal class AddMenuItemIngredientCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<AddMenuItemIngredientCommand, AddMenuItemIngredientResult>
{
    public async Task<AddMenuItemIngredientResult> Handle(AddMenuItemIngredientCommand command, CancellationToken cancellationToken)
    {
        Guid? menuRestaurantId = await dbContext.MenuItems
            .Where(m => m.Id == command.MenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (menuRestaurantId is null)
        {
            throw new NotFoundException("MenuItem", command.MenuItemId);
        }

        currentUser.EnsureTenantAccess(menuRestaurantId.Value);

        Guid? ingredientRestaurantId = await dbContext.Ingredients
            .Where(i => i.Id == command.IngredientId)
            .Select(i => (Guid?)i.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        MenuItemIngredient link = new MenuItemIngredient
        {
            MenuItemId = command.MenuItemId,
            IngredientId = command.IngredientId,
            QuantityRequired = command.QuantityRequired,
            IsOptional = command.IsOptional
        };

        dbContext.MenuItemIngredients.Add(link);

        // Domain event BEFORE SaveChanges so the dispatcher drains
        // it during the same SaveChanges call. The engine handler queries
        // MenuItemId + IngredientId to recompute availability.
        link.AddDomainEvent(new MenuItemIngredientChangedDomainEvent(
            link.Id,
            command.MenuItemId,
            command.IngredientId,
            MenuItemIngredientChangedDomainEvent.ChangeKind.Created));

        await notifier.SaveAndNotifyRelatedAsync(command.MenuItemId, menuRestaurantId.Value, cancellationToken).ConfigureAwait(false);

        if (ingredientRestaurantId is { } ingredientRid)
        {
            await cache.InvalidateIngredientsAsync(ingredientRid, cancellationToken);
        }

        return new AddMenuItemIngredientResult(link.Id);
    }
}
