namespace Catalog.API.Features.IngredientAlternatives.GetIngredientAlternatives;

public record GetIngredientAlternativesResponse(IEnumerable<IngredientAlternativeDto> IngredientAlternatives);

public record IngredientAlternativeDto(
    int Id,
    Guid RestaurantId,
    int OriginalIngredientId,
    int AlternativeIngredientId,
    decimal PriceModifier,
    bool AutoSubstitute);

public class GetIngredientAlternativesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("IngredientAlternatives");

        group.MapGet("/restaurants/{restaurantId:guid}/ingredient-alternatives", async (Guid restaurantId, ISender sender) =>
        {
            var query = new GetIngredientAlternativesQuery(restaurantId);

            var result = await sender.Send(query);
            var response = result.Adapt<GetIngredientAlternativesResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets ingredient alternatives for a restaurant.")
        .WithName("GetIngredientAlternatives")
        .Produces<GetIngredientAlternativesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
