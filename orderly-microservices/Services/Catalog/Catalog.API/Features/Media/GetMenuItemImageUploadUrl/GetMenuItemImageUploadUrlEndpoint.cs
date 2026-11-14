using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NodaTime;

namespace Catalog.API.Features.Media.GetMenuItemImageUploadUrl;

public sealed record GetMenuItemImageUploadUrlRequest(string ContentType);

public sealed record GetMenuItemImageUploadUrlResponse(
    string PresignedUrl,
    string ObjectKey,
    Instant ExpiresAt);

public sealed class GetMenuItemImageUploadUrlEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/image-upload-url", async (
            Guid restaurantId,
            Guid menuItemId,
            GetMenuItemImageUploadUrlRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            GetMenuItemImageUploadUrlCommand command = new(
                RestaurantId: restaurantId,
                MenuItemId: menuItemId,
                ContentType: request.ContentType);

            GetMenuItemImageUploadUrlResult result = await sender.Send(command, cancellationToken);

            return Results.Ok(new GetMenuItemImageUploadUrlResponse(
                PresignedUrl: result.PresignedUrl,
                ObjectKey: result.ObjectKey,
                ExpiresAt: result.ExpiresAt));
        })
        .RequirePermission("menu:edit")
        .WithTags("Media", "MenuItems")
        .WithDescription("Generates a presigned S3/MinIO upload URL for direct client upload of a menu item image.")
        .WithName("GetMenuItemImageUploadUrl")
        .Produces<GetMenuItemImageUploadUrlResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
