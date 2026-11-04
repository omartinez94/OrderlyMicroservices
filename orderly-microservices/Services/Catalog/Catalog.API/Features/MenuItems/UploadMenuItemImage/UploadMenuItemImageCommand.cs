namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

public sealed record UploadMenuItemImageCommand(
    Guid RestaurantId,
    Guid MenuItemId,
    IFormFile File) : ICommand<UploadMenuItemImageResult>;

public sealed record UploadMenuItemImageResult(
    bool Success,
    string ImageUrl);
