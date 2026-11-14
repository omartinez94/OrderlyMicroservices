using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NodaTime;

namespace Catalog.API.Features.Restaurants.UploadRestaurantImage;

public sealed record GetRestaurantImageUploadUrlRequest(
    string AssetType,
    string ContentType);

public sealed record GetRestaurantImageUploadUrlResponse(
    string PresignedUrl,
    string ObjectKey,
    Instant ExpiresAt);

public sealed class GetRestaurantImageUploadUrlEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/restaurants/{restaurantId:guid}/images/upload-url", async (
            Guid restaurantId,
            GetRestaurantImageUploadUrlRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            GetRestaurantImageUploadUrlCommand command = new(
                RestaurantId: restaurantId,
                AssetType: request.AssetType,
                ContentType: request.ContentType);

            GetRestaurantImageUploadUrlResult result = await sender.Send(command, cancellationToken);

            return Results.Ok(new GetRestaurantImageUploadUrlResponse(
                PresignedUrl: result.PresignedUrl,
                ObjectKey: result.ObjectKey,
                ExpiresAt: result.ExpiresAt));
        })
        .RequirePermission("restaurants:edit")
        .WithTags("Media", "Restaurants")
        .WithDescription("Generates a presigned S3/MinIO upload URL for restaurant logo or banner.")
        .WithName("GetRestaurantImageUploadUrl")
        .Produces<GetRestaurantImageUploadUrlResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
