namespace Catalog.API.Features.MenuItems.DeleteMenuItemImage;

public sealed record DeleteMenuItemImageCommand(
    Guid RestaurantId,
    Guid MenuItemId) : ICommand<DeleteMenuItemImageResult>;

public sealed record DeleteMenuItemImageResult(bool Success);
