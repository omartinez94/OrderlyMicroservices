namespace Catalog.API.Features.MenuItems.GetMenuItems;

public record GetMenuItemsRequest(int? SubCategoryId, bool? IsAvailable, int PageNumber = 1, int PageSize = 10);

public record GetMenuItemsResponse(IEnumerable<MenuItemDto> Items, int TotalCount, int PageNumber, int PageSize);

public class GetMenuItemsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/restaurants/{restaurantId:guid}/menu-items", async (Guid restaurantId, [AsParameters] GetMenuItemsRequest request, ISender sender) =>
        {
            var query = new GetMenuItemsQuery
            {
                RestaurantId = restaurantId,
                SubCategoryId = request.SubCategoryId,
                IsAvailable = request.IsAvailable,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            };

            var result = await sender.Send(query);
            return Results.Ok(result.Adapt<GetMenuItemsResponse>());
        })
        .WithTags("MenuItems")
        .WithDescription("Gets a list of menu items for a restaurant.")
        .WithName("GetMenuItems")
        .Produces<GetMenuItemsResponse>(StatusCodes.Status200OK);
    }
}
