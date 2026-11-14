using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Catalog.API.Features.Restaurants.UploadRestaurantImage;

public sealed record ConfirmRestaurantImageUploadRequest(
    string AssetType,
    string ObjectKey);

public sealed record ConfirmRestaurantImageUploadResponse(
    bool Success,
    string ImageUrl);

public sealed class ConfirmRestaurantImageUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/restaurants/{restaurantId:guid}/images/confirm", async (
            Guid restaurantId,
            ConfirmRestaurantImageUploadRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            ConfirmRestaurantImageUploadCommand command = new(
                RestaurantId: restaurantId,
                AssetType: request.AssetType,
                ObjectKey: request.ObjectKey);

            ConfirmRestaurantImageUploadResult result = await sender.Send(command, cancellationToken);

            return Results.Ok(new ConfirmRestaurantImageUploadResponse(
                Success: result.Success,
                ImageUrl: result.ImageUrl));
        })
        .RequirePermission("restaurants:edit")
        .WithTags("Media", "Restaurants")
        .WithDescription("Confirms direct client upload of a restaurant logo or banner, updates the restaurant entity, and returns the public CDN URL.")
        .WithName("ConfirmRestaurantImageUpload")
        .Produces<ConfirmRestaurantImageUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
