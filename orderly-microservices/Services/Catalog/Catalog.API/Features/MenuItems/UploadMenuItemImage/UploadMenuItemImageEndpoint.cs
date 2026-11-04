namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

public sealed record UploadMenuItemImageResponse(bool Success, string ImageUrl);

public sealed class UploadMenuItemImageEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/restaurants/{restaurantId:guid}/menu-items/{id:guid}/image", async (
            Guid restaurantId,
            Guid id,
            IFormFile file,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            UploadMenuItemImageCommand command = new(
                RestaurantId: restaurantId,
                MenuItemId: id,
                File: file);

            UploadMenuItemImageResult result = await sender.Send(command, cancellationToken);
            return Results.Ok(new UploadMenuItemImageResponse(result.Success, result.ImageUrl));
        })
        .DisableAntiforgery()
        .RequirePermission("menu:edit")
        .WithTags("MenuItems")
        .WithDescription("Uploads an image for a menu item, storing it in blob storage and updating ImageUrl.")
        .WithName("UploadMenuItemImage")
        .Produces<UploadMenuItemImageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .Accepts<IFormFile>("multipart/form-data");
    }
}
