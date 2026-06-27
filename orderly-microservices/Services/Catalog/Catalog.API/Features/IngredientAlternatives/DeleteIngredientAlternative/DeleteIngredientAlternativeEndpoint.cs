namespace Catalog.API.Features.IngredientAlternatives.DeleteIngredientAlternative;

public record DeleteIngredientAlternativeResponse(bool Success);

public class DeleteIngredientAlternativeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("IngredientAlternatives");

        group.MapDelete("/restaurants/{restaurantId:guid}/ingredient-alternatives/{id:int}", async (Guid restaurantId, int id, ISender sender) =>
        {
            var command = new DeleteIngredientAlternativeCommand(id, restaurantId);

            var result = await sender.Send(command);
            var response = result.Adapt<DeleteIngredientAlternativeResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes an ingredient alternative.")
        .WithName("DeleteIngredientAlternative")
        .Produces<DeleteIngredientAlternativeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
