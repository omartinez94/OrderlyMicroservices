namespace Catalog.API.Features.Ingredients.GetIngredients;

public record GetIngredientsResponse(IEnumerable<Ingredient> Ingredients);

public class GetIngredientsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Ingredients");

        group.MapGet("/restaurants/{restaurantId:guid}/ingredients", async (Guid restaurantId, [AsParameters] GetIngredientsQuery query, ISender sender) =>
        {
            var command = query with { RestaurantId = restaurantId };
            var result = await sender.Send(command);
            var response = result.Adapt<GetIngredientsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets ingredients for a restaurant.")
        .WithName("GetIngredients")
        .Produces<GetIngredientsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
