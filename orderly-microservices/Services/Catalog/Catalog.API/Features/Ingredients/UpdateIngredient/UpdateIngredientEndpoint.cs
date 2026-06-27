namespace Catalog.API.Features.Ingredients.UpdateIngredient;

public record UpdateIngredientRequest(
    string Name,
    string Unit,
    decimal CurrentStock,
    decimal MinimumStock,
    bool IsAvailable);

public record UpdateIngredientResponse(bool IsSuccess);

public class UpdateIngredientEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Ingredients");

        group.MapPut("/restaurants/{restaurantId:guid}/ingredients/{id:int}", async (Guid restaurantId, int id, UpdateIngredientRequest request, ISender sender) =>
        {
            var command = request.Adapt<UpdateIngredientCommand>() with { RestaurantId = restaurantId, Id = id };
            var result = await sender.Send(command);
            var response = result.Adapt<UpdateIngredientResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates an ingredient for a restaurant.")
        .WithName("UpdateIngredient")
        .Produces<UpdateIngredientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
