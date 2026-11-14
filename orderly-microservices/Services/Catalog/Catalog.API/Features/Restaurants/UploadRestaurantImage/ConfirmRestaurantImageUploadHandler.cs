using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.API.Features.Restaurants.UploadRestaurantImage;

public sealed record ConfirmRestaurantImageUploadCommand(
    Guid RestaurantId,
    string AssetType,
    string ObjectKey) : ICommand<ConfirmRestaurantImageUploadResult>;

public sealed record ConfirmRestaurantImageUploadResult(
    bool Success,
    string ImageUrl);

public sealed class ConfirmRestaurantImageUploadValidator : AbstractValidator<ConfirmRestaurantImageUploadCommand>
{
    private static readonly string[] AllowedAssetTypes =
    [
        "logo",
        "banner"
    ];

    public ConfirmRestaurantImageUploadValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId is required.");

        RuleFor(x => x.AssetType)
            .NotEmpty()
            .WithMessage("AssetType is required.")
            .Must(at => AllowedAssetTypes.Contains(at, StringComparer.OrdinalIgnoreCase))
            .WithMessage("AssetType must be either 'logo' or 'banner'.");

        RuleFor(x => x.ObjectKey)
            .NotEmpty()
            .WithMessage("ObjectKey is required.")
            .Must((cmd, key) => key != null && key.StartsWith($"restaurants/{cmd.RestaurantId}/assets/{cmd.AssetType.ToLowerInvariant()}-", StringComparison.OrdinalIgnoreCase))
            .WithMessage("ObjectKey does not match the expected path prefix for this restaurant asset.");
    }
}

public sealed class ConfirmRestaurantImageUploadHandler(
    CatalogDbContext dbContext,
    IObjectStorageService objectStorage,
    ILogger<ConfirmRestaurantImageUploadHandler> logger,
    ICurrentUser? currentUser = null) : ICommandHandler<ConfirmRestaurantImageUploadCommand, ConfirmRestaurantImageUploadResult>
{
    public async Task<ConfirmRestaurantImageUploadResult> Handle(
        ConfirmRestaurantImageUploadCommand command,
        CancellationToken cancellationToken)
    {
        currentUser?.EnsureTenantAccess(command.RestaurantId);

        Restaurant? restaurant = await dbContext.Restaurants
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(command.RestaurantId);
        }

        bool exists = await objectStorage.ObjectExistsAsync(command.ObjectKey, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new BadRequestException("Uploaded image was not found in storage. Ensure direct client upload completed successfully.");
        }

        string publicUrl = objectStorage.GetPublicUrl(command.ObjectKey);
        string? previousUrl = null;

        if (string.Equals(command.AssetType, "logo", StringComparison.OrdinalIgnoreCase))
        {
            previousUrl = restaurant.LogoUrl;
            restaurant.LogoUrl = publicUrl;
        }
        else
        {
            previousUrl = restaurant.BannerUrl;
            restaurant.BannerUrl = publicUrl;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Best effort cleanup of previous image
        if (!string.IsNullOrEmpty(previousUrl) && !string.Equals(previousUrl, publicUrl, StringComparison.OrdinalIgnoreCase))
        {
            int idx = previousUrl.IndexOf("restaurants/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string previousKey = previousUrl.Substring(idx);
                try
                {
                    await objectStorage.DeleteAsync(previousKey, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete previous restaurant asset {ObjectKey} from storage", previousKey);
                }
            }
        }

        return new ConfirmRestaurantImageUploadResult(Success: true, ImageUrl: publicUrl);
    }
}
