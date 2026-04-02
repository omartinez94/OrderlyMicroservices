namespace Catalog.API.Restaurants.CreateRestaurant;

public record CreateRestaurantRequest(
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

public record CreateRestaurantResponse(Guid Id);

public class CreateRestaurantEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/restaurants", async (CreateRestaurantRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateRestaurantCommand>();
            var result = await sender.Send(command);
            var response = result.Adapt<CreateRestaurantResponse>();

            return Results.Created($"/restaurants/{response.Id}", response);
        })
        .WithDescription("Creates a new restaurant.")
        .WithName("CreateRestaurant")
        .Produces<CreateRestaurantResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
