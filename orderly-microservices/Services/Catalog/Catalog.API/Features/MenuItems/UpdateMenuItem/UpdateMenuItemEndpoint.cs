namespace Catalog.API.Features.MenuItems.UpdateMenuItem;

public record UpdateMenuItemRequest(
    int? SubCategoryId,
    string Name,
    string Description,
    decimal BasePrice,
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
    int DisplayOrder,
    string? ImageUrl = null,
    int? DefaultCourseNumber = null);

public record UpdateMenuItemResponse(bool Success);

public class UpdateMenuItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/menu-items/{id:guid}", async (Guid id, UpdateMenuItemRequest request, ISender sender) =>
        {
            UpdateMenuItemCommand? command = request.Adapt<UpdateMenuItemCommand>();
            command.Id = id;

            UpdateMenuItemResult? result = await sender.Send(command);
            UpdateMenuItemResponse? response = result.Adapt<UpdateMenuItemResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("menu:edit")
        .WithTags("MenuItems")
        .WithDescription("Updates a menu item.")
        .WithName("UpdateMenuItem")
        .Produces<UpdateMenuItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
