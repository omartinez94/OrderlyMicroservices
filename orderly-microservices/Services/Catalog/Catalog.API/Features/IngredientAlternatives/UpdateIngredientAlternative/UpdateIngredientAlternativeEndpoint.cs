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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("IngredientAlternatives");

        group.MapPut("/restaurants/{restaurantId:guid}/ingredient-alternatives/{id:int}", async (Guid restaurantId, int id, UpdateIngredientAlternativeRequest request, ISender sender) =>
        {
            UpdateIngredientAlternativeCommand? command = new UpdateIngredientAlternativeCommand(
                id,
                restaurantId,
                request.OriginalIngredientId,
                request.AlternativeIngredientId,
                request.PriceModifier,
                request.AutoSubstitute);

            UpdateIngredientAlternativeResult? result = await sender.Send(command);
            UpdateIngredientAlternativeResponse? response = result.Adapt<UpdateIngredientAlternativeResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates an ingredient alternative.")
        .WithName("UpdateIngredientAlternative")
        .Produces<UpdateIngredientAlternativeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
