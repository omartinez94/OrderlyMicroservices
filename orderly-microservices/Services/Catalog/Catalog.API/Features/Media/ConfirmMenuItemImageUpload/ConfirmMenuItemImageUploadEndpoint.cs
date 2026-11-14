using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Catalog.API.Features.Media.ConfirmMenuItemImageUpload;

public sealed record ConfirmMenuItemImageUploadResponse(bool Success, string ImageUrl);

public sealed class ConfirmMenuItemImageUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/confirm-image", async (
            Guid restaurantId,
            Guid menuItemId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            ConfirmMenuItemImageUploadCommand command = new(
                RestaurantId: restaurantId,
                MenuItemId: menuItemId);

            ConfirmMenuItemImageUploadResult result = await sender.Send(command, cancellationToken);

            return Results.Ok(new ConfirmMenuItemImageUploadResponse(
                Success: result.Success,
                ImageUrl: result.ImageUrl));
        })
        .RequirePermission("menu:edit")
        .WithTags("Media", "MenuItems")
        .WithDescription("Confirms direct client upload of a menu item image, verifies presence in storage, updates ImageUrl, and publishes event.")
        .WithName("ConfirmMenuItemImageUpload")
        .Produces<ConfirmMenuItemImageUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
