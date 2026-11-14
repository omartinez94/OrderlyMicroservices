using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Restaurants.UploadRestaurantImage;

public sealed record GetRestaurantImageUploadUrlCommand(
    Guid RestaurantId,
    string AssetType,
    string ContentType) : ICommand<GetRestaurantImageUploadUrlResult>;

public sealed record GetRestaurantImageUploadUrlResult(
    string PresignedUrl,
    string ObjectKey,
    Instant ExpiresAt);

public sealed class GetRestaurantImageUploadUrlValidator : AbstractValidator<GetRestaurantImageUploadUrlCommand>
{
    private static readonly string[] AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    private static readonly string[] AllowedAssetTypes =
    [
        "logo",
        "banner"
    ];

    public GetRestaurantImageUploadUrlValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId is required.");

        RuleFor(x => x.AssetType)
            .NotEmpty()
            .WithMessage("AssetType is required.")
            .Must(at => AllowedAssetTypes.Contains(at, StringComparer.OrdinalIgnoreCase))
            .WithMessage("AssetType must be either 'logo' or 'banner'.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessage("ContentType is required.")
            .Must(ct => AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
            .WithMessage("ContentType must be one of: image/jpeg, image/png, image/webp.");
    }
}

public sealed class GetRestaurantImageUploadUrlHandler(
    CatalogDbContext dbContext,
    IObjectStorageService objectStorage,
    IOptions<ObjectStorageSettings> storageOptions,
    IClock clock,
    ICurrentUser? currentUser = null) : ICommandHandler<GetRestaurantImageUploadUrlCommand, GetRestaurantImageUploadUrlResult>
{
    public async Task<GetRestaurantImageUploadUrlResult> Handle(
        GetRestaurantImageUploadUrlCommand command,
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

        string extension = command.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        string normalizedAssetType = command.AssetType.ToLowerInvariant();
        string objectKey = $"restaurants/{command.RestaurantId}/assets/{normalizedAssetType}-{Guid.NewGuid()}{extension}";

        string presignedUrl = await objectStorage.GeneratePresignedUploadUrlAsync(
            objectKey,
            command.ContentType,
            cancellationToken).ConfigureAwait(false);

        int expiryMinutes = storageOptions.Value.PresignedUrlExpiryMinutes > 0
            ? storageOptions.Value.PresignedUrlExpiryMinutes
            : 15;

        Instant expiresAt = clock.GetCurrentInstant().Plus(Duration.FromMinutes(expiryMinutes));

        return new GetRestaurantImageUploadUrlResult(
            PresignedUrl: presignedUrl,
            ObjectKey: objectKey,
            ExpiresAt: expiresAt);
    }
}
