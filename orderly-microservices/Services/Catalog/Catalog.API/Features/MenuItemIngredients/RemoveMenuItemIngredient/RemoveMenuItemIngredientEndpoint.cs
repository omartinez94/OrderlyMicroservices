namespace Catalog.API.Features.MenuItemIngredients.RemoveMenuItemIngredient;

public class RemoveMenuItemIngredientEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuItemIngredients");

        group.MapDelete("/menu-items/{menuItemId}/ingredients/{id}", async (Guid menuItemId, int id, ISender sender) =>
        {
            RemoveMenuItemIngredientCommand? command = new RemoveMenuItemIngredientCommand(menuItemId, id);
            await sender.Send(command);

            return Results.NoContent();
        })
        .WithDescription("Removes an ingredient from a menu item.")
        .WithName("RemoveMenuItemIngredient")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
