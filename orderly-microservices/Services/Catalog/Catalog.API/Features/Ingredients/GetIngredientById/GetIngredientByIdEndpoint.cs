namespace Catalog.API.Features.Ingredients.GetIngredientById;

public record GetIngredientByIdResponse(Ingredient Ingredient);

public class GetIngredientByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Ingredients");

        group.MapGet("/restaurants/{restaurantId:guid}/ingredients/{id:int}", async (Guid restaurantId, int id, ISender sender) =>
        {
            var result = await sender.Send(new GetIngredientByIdQuery(restaurantId, id));
            var response = result.Adapt<GetIngredientByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets an ingredient by id for a restaurant.")
        .WithName("GetIngredientById")
        .Produces<GetIngredientByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
