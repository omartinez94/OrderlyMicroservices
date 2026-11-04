using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItems.PatchMenuItemAvailability;

internal sealed class PatchMenuItemAvailabilityHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    ICurrentUser currentUser) : ICommandHandler<PatchMenuItemAvailabilityCommand, PatchMenuItemAvailabilityResult>
{
    public async Task<PatchMenuItemAvailabilityResult> Handle(
        PatchMenuItemAvailabilityCommand command,
        CancellationToken cancellationToken)
    {
        currentUser.EnsureTenantAccess(command.RestaurantId);

        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (menuItem is null || menuItem.RestaurantId != command.RestaurantId)
        {
            throw new NotFoundException(nameof(MenuItem), command.Id);
        }

        AvailabilityStatus parsedStatus = Enum.Parse<AvailabilityStatus>(command.AvailabilityStatus, ignoreCase: true);

        menuItem.AvailabilityStatus = parsedStatus;
        menuItem.IsAvailable = parsedStatus != AvailabilityStatus.Unavailable;

        await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Updated, cancellationToken).ConfigureAwait(false);

        return new PatchMenuItemAvailabilityResult(
            Success: true,
            IsAvailable: menuItem.IsAvailable,
            AvailabilityStatus: menuItem.AvailabilityStatus.ToString());
    }
}
