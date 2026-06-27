namespace Catalog.API.Features.IngredientAlternatives.UpdateIngredientAlternative;

public record UpdateIngredientAlternativeRequest(
    int OriginalIngredientId,
    int AlternativeIngredientId,
    decimal PriceModifier,
    bool AutoSubstitute);

public record UpdateIngredientAlternativeResponse(bool Success);

public class UpdateIngredientAlternativeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("IngredientAlternatives");

        group.MapPut("/restaurants/{restaurantId:guid}/ingredient-alternatives/{id:int}", async (Guid restaurantId, int id, UpdateIngredientAlternativeRequest request, ISender sender) =>
        {
            var command = new UpdateIngredientAlternativeCommand(
                id,
                restaurantId,
                request.OriginalIngredientId,
                request.AlternativeIngredientId,
                request.PriceModifier,
                request.AutoSubstitute);

            var result = await sender.Send(command);
            var response = result.Adapt<UpdateIngredientAlternativeResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates an ingredient alternative.")
        .WithName("UpdateIngredientAlternative")
        .Produces<UpdateIngredientAlternativeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
