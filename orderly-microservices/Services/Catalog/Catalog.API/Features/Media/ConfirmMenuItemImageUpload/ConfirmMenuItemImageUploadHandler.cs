using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.API.Features.Media.ConfirmMenuItemImageUpload;

public sealed record ConfirmMenuItemImageUploadCommand(
    Guid RestaurantId,
    Guid MenuItemId) : ICommand<ConfirmMenuItemImageUploadResult>;

public sealed record ConfirmMenuItemImageUploadResult(
    bool Success,
    string ImageUrl);

public sealed class ConfirmMenuItemImageUploadValidator : AbstractValidator<ConfirmMenuItemImageUploadCommand>
{
    public ConfirmMenuItemImageUploadValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId is required.");

        RuleFor(x => x.MenuItemId)
            .NotEmpty()
            .WithMessage("MenuItemId is required.");
    }
}

public sealed class ConfirmMenuItemImageUploadHandler(
    CatalogDbContext dbContext,
    IObjectStorageService objectStorage,
    IMenuItemMutationNotifier notifier,
    IOutboxPublisher outbox,
    ILogger<ConfirmMenuItemImageUploadHandler> logger,
    ICurrentUser? currentUser = null) : ICommandHandler<ConfirmMenuItemImageUploadCommand, ConfirmMenuItemImageUploadResult>
{
    public async Task<ConfirmMenuItemImageUploadResult> Handle(
        ConfirmMenuItemImageUploadCommand command,
        CancellationToken cancellationToken)
    {
        currentUser?.EnsureTenantAccess(command.RestaurantId);

        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.MenuItemId && m.RestaurantId == command.RestaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), command.MenuItemId);
        }

        if (menuItem.ImageUploadStatus != ImageUploadStatus.Pending || string.IsNullOrWhiteSpace(menuItem.PendingImageKey))
        {
            throw new BadRequestException("No pending image upload found for this menu item.");
        }

        string pendingKey = menuItem.PendingImageKey;

        bool exists = await objectStorage.ObjectExistsAsync(pendingKey, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new BadRequestException("Uploaded image was not found in storage. Ensure direct client upload completed successfully.");
        }

        string previousImageUrl = menuItem.ImageUrl;
        string publicUrl = objectStorage.GetPublicUrl(pendingKey);

        menuItem.ImageUrl = publicUrl;
        menuItem.PendingImageKey = null;
        menuItem.ImageUploadStatus = ImageUploadStatus.Confirmed;

        // Publish event for asynchronous consumers (e.g., thumbnail generation, search indexing)
        await outbox.PublishAsync(new MenuItemImageUploadedIntegrationEvent
        {
            RestaurantId = command.RestaurantId,
            MenuItemId = command.MenuItemId,
            ImageUrl = publicUrl,
            ObjectKey = pendingKey
        }, cancellationToken).ConfigureAwait(false);

        // Atomic commit and cache invalidation via mutation notifier
        await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Updated, cancellationToken).ConfigureAwait(false);

        // Best effort cleanup of previous image
        if (!string.IsNullOrEmpty(previousImageUrl) && !string.Equals(previousImageUrl, publicUrl, StringComparison.OrdinalIgnoreCase))
        {
            int idx = previousImageUrl.IndexOf("restaurants/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string previousKey = previousImageUrl.Substring(idx);
                try
                {
                    await objectStorage.DeleteAsync(previousKey, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete previous object {ObjectKey} from storage", previousKey);
                }
            }
        }

        return new ConfirmMenuItemImageUploadResult(Success: true, ImageUrl: publicUrl);
    }
}
