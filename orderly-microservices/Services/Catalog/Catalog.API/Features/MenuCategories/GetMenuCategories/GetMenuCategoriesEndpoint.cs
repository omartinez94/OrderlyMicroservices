namespace Catalog.API.Features.MenuCategories.GetMenuCategories;

public record GetMenuCategoriesResponse(IEnumerable<MenuCategoryDto> MenuCategories);

public class GetMenuCategoriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuCategories");

        group.MapGet("/restaurants/{restaurantId:guid}/menu-categories", async (Guid restaurantId, ISender sender) =>
        {
            var query = new GetMenuCategoriesQuery(restaurantId);
            var result = await sender.Send(query);
            var response = result.Adapt<GetMenuCategoriesResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets all menu categories for a restaurant.")
        .WithName("GetMenuCategories")
        .Produces<GetMenuCategoriesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
