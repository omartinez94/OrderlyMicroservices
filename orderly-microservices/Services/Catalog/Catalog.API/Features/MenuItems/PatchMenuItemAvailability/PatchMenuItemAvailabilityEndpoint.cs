namespace Catalog.API.Features.MenuItems.PatchMenuItemAvailability;

public sealed record PatchMenuItemAvailabilityRequest(string AvailabilityStatus);

public sealed record PatchMenuItemAvailabilityResponse(
    bool Success,
    bool IsAvailable,
    string AvailabilityStatus);

public sealed class PatchMenuItemAvailabilityEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("/v1/restaurants/{restaurantId:guid}/menu-items/{id:guid}/availability", async (
            Guid restaurantId,
            Guid id,
            PatchMenuItemAvailabilityRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            PatchMenuItemAvailabilityCommand command = new(
                RestaurantId: restaurantId,
                Id: id,
                AvailabilityStatus: request.AvailabilityStatus);

            PatchMenuItemAvailabilityResult result = await sender.Send(command, cancellationToken);
            PatchMenuItemAvailabilityResponse response = result.Adapt<PatchMenuItemAvailabilityResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("menu:toggle")
        .WithTags("MenuItems")
        .WithDescription("Updates a menu item's availability status and derives its IsAvailable flag.")
        .WithName("PatchMenuItemAvailability")
        .Produces<PatchMenuItemAvailabilityResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
