using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.ComboItems.UpdateComboItem;

/// <summary>
/// Updates <see cref="ComboItem.Quantity"/> and/or
/// <see cref="ComboItem.IsOptional"/> for one row in a combo menu
/// item's recipe list. The combo <c>ComboMenuItemId</c> and
/// <c>IncludedMenuItemId</c> are immutable on update (they identify the
/// relationship) — to change either, delete the row and re-create it.
/// </summary>
/// <param name="Id">Primary key of the combo-item row.</param>
/// <param name="Quantity">New quantity (must be &gt; 0).</param>
/// <param name="IsOptional">Whether the included item is now optional.</param>
public record UpdateComboItemCommand(
    int Id,
    int Quantity,
    bool IsOptional) : ICommand<UpdateComboItemResult>;

public record UpdateComboItemResult(bool Success);

public class UpdateComboItemCommandValidator : AbstractValidator<UpdateComboItemCommand>
{
    public UpdateComboItemCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0");
    }
}

internal class UpdateComboItemCommandHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<UpdateComboItemCommand, UpdateComboItemResult>
{
    public async Task<UpdateComboItemResult> Handle(UpdateComboItemCommand command, CancellationToken cancellationToken)
    {
        ComboItem? comboItem = await dbContext.ComboItems.FindAsync([command.Id], cancellationToken);

        if (comboItem is null)
        {
            throw new NotFoundException(nameof(ComboItem), command.Id);
        }

        // The plan (§7 Phase 4) requires us to validate the included menu
        // item still exists — a combo row pointing at a soft-deleted menu
        // item would surface as a broken menu at order time.
        bool includedMenuItemExists = await dbContext.MenuItems
            .Where(m => m.Id == comboItem.IncludedMenuItemId && !m.IsDeleted)
            .AnyAsync(cancellationToken);
        if (!includedMenuItemExists)
        {
            throw new NotFoundException("IncludedMenuItem", comboItem.IncludedMenuItemId);
        }

        Guid restaurantId = await dbContext.MenuItems
            .Where(m => m.Id == comboItem.ComboMenuItemId && !m.IsDeleted)
            .Select(m => m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurantId != Guid.Empty)
        {
            currentUser.EnsureTenantAccess(restaurantId);
        }

        comboItem.Quantity = command.Quantity;
        comboItem.IsOptional = command.IsOptional;

        await notifier.SaveAndNotifyRelatedAsync(comboItem.ComboMenuItemId, restaurantId, cancellationToken).ConfigureAwait(false);

        return new UpdateComboItemResult(true);
    }
}
