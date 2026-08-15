namespace Catalog.API.Features.PriceHistories.GetPriceHistory;

public record GetPriceHistoryRequest(
    Guid? MenuItemId,
    PriceType? PriceType,
    Instant? From,
    Instant? To);

public record PriceHistoryDto(
    int Id,
    Guid ChangedByUserId,
    Instant CreatedAt,
    Instant EffectiveDate,
    decimal NewPrice,
    decimal OldPrice,
    PriceType PriceType,
    string Reason,
    Guid RestaurantId,
    int? IngredientAlternativeId,
    Guid? MenuItemId,
    int? VariationId);

public record GetPriceHistoryResponse(IEnumerable<PriceHistoryDto> PriceHistories);

public class GetPriceHistoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("PriceHistories");

        group.MapGet("/restaurants/{restaurantId}/price-history", async (
            Guid restaurantId,
            [AsParameters] GetPriceHistoryRequest request,
            ISender sender) =>
        {
            var query = new GetPriceHistoryQuery(
                restaurantId,
                request.MenuItemId,
                request.PriceType,
                request.From,
                request.To);

            var result = await sender.Send(query);
            var response = result.Adapt<GetPriceHistoryResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets the price history for a restaurant.")
        .WithName("GetPriceHistory")
        .Produces<GetPriceHistoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
