namespace Catalog.API.Features.PriceHistories.GetPriceHistory;

/// <remarks>
/// <c>From</c>/<c>To</c> are <see cref="DateTimeOffset"/> rather than NodaTime
/// <c>Instant</c> because Minimal API can only bind an <c>[AsParameters]</c>
/// property from the query string when its type is parsable
/// (<c>IParsable&lt;T&gt;</c> or a static <c>TryParse</c>). <c>Instant</c> is
/// neither, so the binder infers it as a body parameter and throws on GET while
/// the endpoint graph is being built — which fails every route in the service,
/// not just this one. <c>DateTimeOffset</c> is <c>IParsable</c>, round-trips the
/// same ISO-8601 instant, and carries the offset needed to convert without
/// guessing a zone. Converted at the handler boundary via
/// <c>Instant.FromDateTimeOffset</c>.
/// </remarks>
public record GetPriceHistoryRequest(
    Guid? MenuItemId,
    PriceType? PriceType,
    DateTimeOffset? From,
    DateTimeOffset? To);

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
                request.From is { } from ? Instant.FromDateTimeOffset(from) : null,
                request.To is { } to ? Instant.FromDateTimeOffset(to) : null);

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
