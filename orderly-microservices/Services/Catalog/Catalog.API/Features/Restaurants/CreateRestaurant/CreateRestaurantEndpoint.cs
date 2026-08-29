namespace Catalog.API.Features.Restaurants.CreateRestaurant;

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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Restaurants");

        group.MapPost("/restaurants", async (CreateRestaurantRequest request, ClaimsPrincipal user, ISender sender) =>
        {
            CreateRestaurantCommand? command = request.Adapt<CreateRestaurantCommand>();

            CreateRestaurantResult? result = await sender.Send(command);
            CreateRestaurantResponse? response = result.Adapt<CreateRestaurantResponse>();

            return Results.Created($"/v1/restaurants/{response.Id}", response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Creates a new restaurant.")
        .WithName("CreateRestaurant")
        .Produces<CreateRestaurantResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
