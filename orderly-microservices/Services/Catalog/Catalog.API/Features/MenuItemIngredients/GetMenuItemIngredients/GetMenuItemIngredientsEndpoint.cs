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
        var group = app.MapGroup("/api/v1").WithTags("MenuItemIngredients");

        group.MapGet("/menu-items/{menuItemId}/ingredients", async (Guid menuItemId, ISender sender) =>
        {
            var query = new GetMenuItemIngredientsQuery(menuItemId);
            var result = await sender.Send(query);
            var response = result.Adapt<GetMenuItemIngredientsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets all ingredients for a menu item.")
        .WithName("GetMenuItemIngredients")
        .Produces<GetMenuItemIngredientsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
