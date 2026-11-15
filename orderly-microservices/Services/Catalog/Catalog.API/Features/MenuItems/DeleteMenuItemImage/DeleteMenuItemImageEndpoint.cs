namespace Catalog.API.Features.MenuItems.DeleteMenuItemImage;

public sealed record DeleteMenuItemImageResponse(bool Success);

public sealed class DeleteMenuItemImageEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/v1/restaurants/{restaurantId:guid}/menu-items/{id:guid}/image", async (
            Guid restaurantId,
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            DeleteMenuItemImageCommand command = new(
                RestaurantId: restaurantId,
                MenuItemId: id);

            DeleteMenuItemImageResult result = await sender.Send(command, cancellationToken);
            return Results.Ok(new DeleteMenuItemImageResponse(result.Success));
        })
        .RequirePermission("menu:edit")
        .WithTags("MenuItems")
        .WithDescription("Deletes the image for a menu item, clearing ImageUrl and removing the blob.")
        .WithName("DeleteMenuItemImage")
        .Produces<DeleteMenuItemImageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
