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
    ICatalogCache cache) : ICommandHandler<DeleteComboItemCommand, DeleteComboItemResult>
{
    public async Task<DeleteComboItemResult> Handle(DeleteComboItemCommand command, CancellationToken cancellationToken)
    {
        var comboItem = await dbContext.ComboItems
            .FindAsync([command.Id], cancellationToken);

        if (comboItem is null)
        {
            throw new NotFoundException(nameof(ComboItem), command.Id);
        }

        var restaurantId = await dbContext.MenuItems
            .Where(m => m.Id == comboItem.ComboMenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.ComboItems.Remove(comboItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (restaurantId is not null)
        {
            await cache.InvalidateMenuAsync(restaurantId.Value, cancellationToken);
        }

        return new DeleteComboItemResult(true);
    }
}
