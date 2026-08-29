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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuItemIngredients");

        group.MapPost("/menu-items/{menuItemId}/ingredients", async (Guid menuItemId, AddMenuItemIngredientRequest request, ISender sender) =>
        {
            AddMenuItemIngredientCommand? command = new AddMenuItemIngredientCommand(menuItemId, request.IngredientId, request.QuantityRequired, request.IsOptional);
            AddMenuItemIngredientResult? result = await sender.Send(command);
            AddMenuItemIngredientResponse? response = result.Adapt<AddMenuItemIngredientResponse>();

            return Results.Created($"/v1/menu-items/{menuItemId}/ingredients/{response.Id}", response);
        })
        .WithDescription("Adds an ingredient to a menu item.")
        .WithName("AddMenuItemIngredient")
        .Produces<AddMenuItemIngredientResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
