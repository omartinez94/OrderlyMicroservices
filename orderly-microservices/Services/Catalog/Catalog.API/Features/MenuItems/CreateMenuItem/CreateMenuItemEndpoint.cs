namespace Catalog.API.Features.MenuItems.CreateMenuItem;

public record CreateMenuItemRequest(
    int? SubCategoryId,
    string Name,
    string Description,
    decimal BasePrice,
    string ImageUrl,
    int PrepTimeMinutes,
    int PrepTimeMaxMinutes,
    ItemType ItemType,
    bool IsAvailable,
    AvailabilityStatus AvailabilityStatus,
    LocalDate? SeasonStartDate,
    LocalDate? SeasonEndDate,
    decimal? PromoPrice,
    Instant? PromoStartDate,
    Instant? PromoEndDate,
    int DisplayOrder);

public record CreateMenuItemResponse(Guid Id);

public class CreateMenuItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/restaurants/{restaurantId:guid}/menu-items", async (Guid restaurantId, CreateMenuItemRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateMenuItemCommand>();
            command.RestaurantId = restaurantId;

            var result = await sender.Send(command);
            var response = result.Adapt<CreateMenuItemResponse>();

            return Results.Created($"/api/v1/menu-items/{response.Id}", response);
        })
        .WithTags("MenuItems")
        .WithDescription("Creates a new menu item for a restaurant.")
        .WithName("CreateMenuItem")
        .Produces<CreateMenuItemResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
