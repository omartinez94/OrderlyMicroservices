namespace Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalytics;

/// <remarks>
/// <c>From</c>/<c>To</c> are <see cref="DateOnly"/> rather than NodaTime
/// <c>LocalDate</c> because Minimal API can only bind an <c>[AsParameters]</c>
/// property from the query string when its type is parsable. <c>LocalDate</c>
/// is not, so the binder infers it as a body parameter and throws on GET while
/// building the endpoint graph — which fails every route in the service. Same
/// ISO-8601 wire format; converted to <c>LocalDate</c> at the handler boundary.
/// Note this applies only to *query binding*: the <c>LocalDate</c> on the
/// response DTO below is fine, being serialised by NodaTime's JSON converters.
/// </remarks>
public record GetMenuItemAnalyticsRequest(
    Guid? MenuItemId,
    DateOnly? From,
    DateOnly? To,
    int PageIndex = 0,
    int PageSize = 20);

public record MenuItemAnalyticsDto(
    int Id,
    Guid MenuItemId,
    Guid RestaurantId,
    LocalDate AnalysisDate,
    int TimesOrdered,
    int TimesModified,
    int TimesOutOfStock,
    decimal TotalRevenue,
    decimal AvgPrepTimeMinutes,
    int MorningOrders,
    int AfternoonOrders,
    int EveningOrders,
    int NightOrders);

public record GetMenuItemAnalyticsResponse(
    IEnumerable<MenuItemAnalyticsDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public class GetMenuItemAnalyticsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemAnalytics");

        group.MapGet("/restaurants/{restaurantId}/analytics/menu-items", async (
            Guid restaurantId,
            [AsParameters] GetMenuItemAnalyticsRequest request,
            ISender sender) =>
        {
            var query = new GetMenuItemAnalyticsQuery(
                restaurantId,
                request.MenuItemId,
                request.From is { } from ? LocalDate.FromDateOnly(from) : null,
                request.To is { } to ? LocalDate.FromDateOnly(to) : null,
                request.PageIndex,
                request.PageSize);

            var result = await sender.Send(query);
            return Results.Ok(result.Adapt<GetMenuItemAnalyticsResponse>());
        })
        .WithDescription("Gets aggregated menu item analytics for a restaurant.")
        .WithName("GetMenuItemAnalytics")
        .Produces<GetMenuItemAnalyticsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}