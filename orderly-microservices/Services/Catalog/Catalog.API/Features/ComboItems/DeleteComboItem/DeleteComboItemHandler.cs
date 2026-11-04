using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.ComboItems.DeleteComboItem;

public record DeleteComboItemCommand(int Id) : ICommand<DeleteComboItemResult>;

public record DeleteComboItemResult(bool Success);

public class DeleteComboItemCommandValidator : AbstractValidator<DeleteComboItemCommand>
{
    public DeleteComboItemCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class DeleteComboItemCommandHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<DeleteComboItemCommand, DeleteComboItemResult>
{
    public async Task<DeleteComboItemResult> Handle(DeleteComboItemCommand command, CancellationToken cancellationToken)
    {
        ComboItem? comboItem = await dbContext.ComboItems
            .FindAsync([command.Id], cancellationToken);

        if (comboItem is null)
        {
            throw new NotFoundException(nameof(ComboItem), command.Id);
        }

        Guid restaurantId = await dbContext.MenuItems
            .Where(m => m.Id == comboItem.ComboMenuItemId && !m.IsDeleted)
            .Select(m => m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurantId != Guid.Empty)
        {
            currentUser.EnsureTenantAccess(restaurantId);
        }

        dbContext.ComboItems.Remove(comboItem);
        await notifier.SaveAndNotifyRelatedAsync(comboItem.ComboMenuItemId, restaurantId, cancellationToken).ConfigureAwait(false);

        return new DeleteComboItemResult(true);
    }
}
