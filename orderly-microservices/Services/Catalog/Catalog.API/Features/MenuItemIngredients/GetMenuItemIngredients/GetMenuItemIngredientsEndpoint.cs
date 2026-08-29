namespace Catalog.API.Features.MenuItemIngredients.GetMenuItemIngredients;

public record GetMenuItemIngredientsResponse(IEnumerable<MenuItemIngredientDto> Ingredients);

public record MenuItemIngredientDto(
    int Id,
    Guid MenuItemId,
    int IngredientId,
    decimal QuantityRequired,
    bool IsOptional);

public class GetMenuItemIngredientsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuItemIngredients");

        group.MapGet("/menu-items/{menuItemId}/ingredients", async (Guid menuItemId, ISender sender) =>
        {
            GetMenuItemIngredientsQuery? query = new GetMenuItemIngredientsQuery(menuItemId);
            GetMenuItemIngredientsResult? result = await sender.Send(query);
            GetMenuItemIngredientsResponse? response = result.Adapt<GetMenuItemIngredientsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets all ingredients for a menu item.")
        .WithName("GetMenuItemIngredients")
        .Produces<GetMenuItemIngredientsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
