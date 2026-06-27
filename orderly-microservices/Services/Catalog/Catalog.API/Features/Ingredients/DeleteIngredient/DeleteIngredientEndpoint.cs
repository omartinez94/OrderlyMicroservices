namespace Catalog.API.Features.Ingredients.DeleteIngredient;

public record DeleteIngredientResponse(bool IsSuccess);

public class DeleteIngredientEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Ingredients");

        group.MapDelete("/restaurants/{restaurantId:guid}/ingredients/{id:int}", async (Guid restaurantId, int id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteIngredientCommand(restaurantId, id));
            var response = result.Adapt<DeleteIngredientResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes an ingredient for a restaurant.")
        .WithName("DeleteIngredient")
        .Produces<DeleteIngredientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
