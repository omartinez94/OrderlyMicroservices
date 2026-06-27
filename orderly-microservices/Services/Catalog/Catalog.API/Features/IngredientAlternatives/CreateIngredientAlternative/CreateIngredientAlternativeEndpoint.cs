namespace Catalog.API.Features.IngredientAlternatives.CreateIngredientAlternative;

public record CreateIngredientAlternativeRequest(
    int OriginalIngredientId,
    int AlternativeIngredientId,
    decimal PriceModifier,
    bool AutoSubstitute);

public record CreateIngredientAlternativeResponse(int Id);

public class CreateIngredientAlternativeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("IngredientAlternatives");

        group.MapPost("/restaurants/{restaurantId:guid}/ingredient-alternatives", async (Guid restaurantId, CreateIngredientAlternativeRequest request, ISender sender) =>
        {
            var command = new CreateIngredientAlternativeCommand(
                restaurantId,
                request.OriginalIngredientId,
                request.AlternativeIngredientId,
                request.PriceModifier,
                request.AutoSubstitute);

            var result = await sender.Send(command);
            var response = result.Adapt<CreateIngredientAlternativeResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/ingredient-alternatives/{response.Id}", response);
        })
        .WithDescription("Creates a new ingredient alternative.")
        .WithName("CreateIngredientAlternative")
        .Produces<CreateIngredientAlternativeResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
