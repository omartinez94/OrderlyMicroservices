namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

public sealed class UploadMenuItemImageValidator : AbstractValidator<UploadMenuItemImageCommand>
{
    public UploadMenuItemImageValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required.");

        RuleFor(x => x.MenuItemId)
            .NotEmpty().WithMessage("MenuItemId is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("Image file must be provided.")
            .Must(f => f != null && f.Length > 0).WithMessage("Image file must not be empty.")
            .Must(f => f == null || f.Length <= ImageHeaderValidator.MaxFileSizeBytes)
            .WithMessage($"Image file size must not exceed {ImageHeaderValidator.MaxFileSizeBytes / (1024 * 1024)} MB.");
    }
}
