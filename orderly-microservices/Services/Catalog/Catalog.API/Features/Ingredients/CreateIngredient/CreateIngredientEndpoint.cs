namespace Catalog.API.Features.Ingredients.CreateIngredient;

public record CreateIngredientRequest(
    string Name,
    string Unit,
    decimal CurrentStock,
    decimal MinimumStock,
    bool IsAvailable);

public record CreateIngredientResponse(int Id);

public class CreateIngredientEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Ingredients");

        group.MapPost("/restaurants/{restaurantId:guid}/ingredients", async (Guid restaurantId, CreateIngredientRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateIngredientCommand>() with { RestaurantId = restaurantId };
            var result = await sender.Send(command);
            var response = result.Adapt<CreateIngredientResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/ingredients/{response.Id}", response);
        })
        .WithDescription("Creates a new ingredient for a restaurant.")
        .WithName("CreateIngredient")
        .Produces<CreateIngredientResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
