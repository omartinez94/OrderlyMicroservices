using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemVariations.DeleteMenuItemVariation;

public record DeleteMenuItemVariationCommand(int Id) : ICommand<DeleteMenuItemVariationResult>;

public record DeleteMenuItemVariationResult(bool Success);

public class DeleteMenuItemVariationCommandValidator : AbstractValidator<DeleteMenuItemVariationCommand>
{
    public DeleteMenuItemVariationCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class DeleteMenuItemVariationCommandHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<DeleteMenuItemVariationCommand, DeleteMenuItemVariationResult>
{
    public async Task<DeleteMenuItemVariationResult> Handle(DeleteMenuItemVariationCommand command, CancellationToken cancellationToken)
    {
        MenuItemVariation? variation = await dbContext.MenuItemVariations
            .FirstOrDefaultAsync(v => v.Id == command.Id && !v.IsDeleted, cancellationToken);

        if (variation is null)
        {
            throw new NotFoundException("MenuItemVariation", command.Id);
        }

        Guid restaurantId = await dbContext.MenuItems
            .Where(m => m.Id == variation.MenuItemId && !m.IsDeleted)
            .Select(m => m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurantId != Guid.Empty)
        {
            currentUser.EnsureTenantAccess(restaurantId);
        }

        variation.IsDeleted = true;
        variation.DeletedAt = SystemClock.Instance.GetCurrentInstant();

        await notifier.SaveAndNotifyRelatedAsync(variation.MenuItemId, restaurantId, cancellationToken).ConfigureAwait(false);

        return new DeleteMenuItemVariationResult(true);
    }
}
