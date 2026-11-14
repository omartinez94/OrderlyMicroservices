using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Media.GetMenuItemImageUploadUrl;

public sealed record GetMenuItemImageUploadUrlCommand(
    Guid RestaurantId,
    Guid MenuItemId,
    string ContentType) : ICommand<GetMenuItemImageUploadUrlResult>;

public sealed record GetMenuItemImageUploadUrlResult(
    string PresignedUrl,
    string ObjectKey,
    Instant ExpiresAt);

public sealed class GetMenuItemImageUploadUrlValidator : AbstractValidator<GetMenuItemImageUploadUrlCommand>
{
    private static readonly string[] AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public GetMenuItemImageUploadUrlValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId is required.");

        RuleFor(x => x.MenuItemId)
            .NotEmpty()
            .WithMessage("MenuItemId is required.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessage("ContentType is required.")
            .Must(ct => AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
            .WithMessage("ContentType must be one of: image/jpeg, image/png, image/webp.");
    }
}

public sealed class GetMenuItemImageUploadUrlHandler(
    CatalogDbContext dbContext,
    IObjectStorageService objectStorage,
    IOptions<ObjectStorageSettings> storageOptions,
    IClock clock,
    ICurrentUser? currentUser = null) : ICommandHandler<GetMenuItemImageUploadUrlCommand, GetMenuItemImageUploadUrlResult>
{
    public async Task<GetMenuItemImageUploadUrlResult> Handle(
        GetMenuItemImageUploadUrlCommand command,
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

        string extension = command.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        string objectKey = $"restaurants/{command.RestaurantId}/menu-items/{command.MenuItemId}/{Guid.NewGuid()}{extension}";

        string presignedUrl = await objectStorage.GeneratePresignedUploadUrlAsync(
            objectKey,
            command.ContentType,
            cancellationToken).ConfigureAwait(false);

        menuItem.PendingImageKey = objectKey;
        menuItem.ImageUploadStatus = ImageUploadStatus.Pending;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        int expiryMinutes = storageOptions.Value.PresignedUrlExpiryMinutes > 0
            ? storageOptions.Value.PresignedUrlExpiryMinutes
            : 15;

        Instant expiresAt = clock.GetCurrentInstant().Plus(Duration.FromMinutes(expiryMinutes));

        return new GetMenuItemImageUploadUrlResult(
            PresignedUrl: presignedUrl,
            ObjectKey: objectKey,
            ExpiresAt: expiresAt);
    }
}
