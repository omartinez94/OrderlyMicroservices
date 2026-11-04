using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItems.DeleteMenuItem;

public record DeleteMenuItemCommand(Guid Id) : ICommand<DeleteMenuItemResult>;

public record DeleteMenuItemResult(bool Success);

public class DeleteMenuItemCommandValidator : AbstractValidator<DeleteMenuItemCommand>
{
    public DeleteMenuItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
    }
}

internal class DeleteMenuItemCommandHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    ICurrentUser? currentUser = null) : ICommandHandler<DeleteMenuItemCommand, DeleteMenuItemResult>
{
    public async Task<DeleteMenuItemResult> Handle(DeleteMenuItemCommand command, CancellationToken cancellationToken)
    {
        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), command.Id);
        }

        currentUser.EnsureTenantAccess(menuItem.RestaurantId);

        menuItem.IsDeleted = true;
        menuItem.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        menuItem.IsAvailable = false;

        await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Deleted, cancellationToken).ConfigureAwait(false);

        return new DeleteMenuItemResult(true);
    }
}
