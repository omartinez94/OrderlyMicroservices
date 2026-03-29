namespace Catalog.API.Restaurants.UpdateRestaurant;

public record UpdateRestaurantRequest(
    Guid Id,
    int BrandId,
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
        app.MapPut("/restaurants", async (UpdateRestaurantRequest request, ISender sender) =>
        {
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
