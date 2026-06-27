namespace Catalog.API.Features.MenuItemIngredients.AddMenuItemIngredient;

public record AddMenuItemIngredientRequest(
    int IngredientId,
    decimal QuantityRequired,
    bool IsOptional);

public record AddMenuItemIngredientResponse(int Id);

public class AddMenuItemIngredientEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemIngredients");

        group.MapPost("/menu-items/{menuItemId}/ingredients", async (Guid menuItemId, AddMenuItemIngredientRequest request, ISender sender) =>
        {
            var command = new AddMenuItemIngredientCommand(menuItemId, request.IngredientId, request.QuantityRequired, request.IsOptional);
            var result = await sender.Send(command);
            var response = result.Adapt<AddMenuItemIngredientResponse>();

            return Results.Created($"/api/v1/menu-items/{menuItemId}/ingredients/{response.Id}", response);
        })
        .WithDescription("Adds an ingredient to a menu item.")
        .WithName("AddMenuItemIngredient")
        .Produces<AddMenuItemIngredientResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
