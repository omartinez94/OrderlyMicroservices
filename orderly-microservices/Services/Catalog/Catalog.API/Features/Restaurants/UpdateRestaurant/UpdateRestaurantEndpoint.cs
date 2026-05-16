namespace Catalog.API.Features.Restaurants.UpdateRestaurant;

public record UpdateRestaurantRequest(
    Guid Id,
    Guid BrandId,
    string Name,
    string Address,
    string PhoneNumber,
    string Email,
    decimal TaxRate,
    string Currency,
    string TimeZone,
    bool AutoConfirmOrders,
    bool AutoConfirmReservations,
    bool AllowAutoSubstitute,
    int EstimatedTurnoverMinutes);

public record UpdateRestaurantResponse(bool IsSuccess);

public class UpdateRestaurantEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Restaurants");

        group.MapPut("/restaurants/{id}", async (Guid id, UpdateRestaurantRequest request, ISender sender) =>
        {
            if (id != request.Id) return Results.BadRequest();

            var command = request.Adapt<UpdateRestaurantCommand>();
            var result = await sender.Send(command);
            var response = result.Adapt<UpdateRestaurantResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates a restaurant.")
        .WithName("UpdateRestaurant")
        .Produces<UpdateRestaurantResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
