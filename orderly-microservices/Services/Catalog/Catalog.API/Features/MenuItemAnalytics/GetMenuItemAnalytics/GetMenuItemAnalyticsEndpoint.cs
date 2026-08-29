namespace Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalytics;

public record GetMenuItemAnalyticsRequest(
    Guid? MenuItemId,
    LocalDate? From,
    LocalDate? To,
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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuItemAnalytics");

        group.MapGet("/restaurants/{restaurantId}/analytics/menu-items", async (
            Guid restaurantId,
            [AsParameters] GetMenuItemAnalyticsRequest request,
            ISender sender) =>
        {
            GetMenuItemAnalyticsQuery? query = new GetMenuItemAnalyticsQuery(
                restaurantId,
                request.MenuItemId,
                request.From,
                request.To,
                request.PageIndex,
                request.PageSize);

            GetMenuItemAnalyticsResult? result = await sender.Send(query);
            return Results.Ok(result.Adapt<GetMenuItemAnalyticsResponse>());
        })
        .WithDescription("Gets aggregated menu item analytics for a restaurant.")
        .WithName("GetMenuItemAnalytics")
        .Produces<GetMenuItemAnalyticsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}