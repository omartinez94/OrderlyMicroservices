using Catalog.API.Exceptions;
using Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalytics;

namespace Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalyticsById;

public record GetMenuItemAnalyticsByIdRequest(int Id);

public record GetMenuItemAnalyticsByIdResponse(MenuItemAnalyticsDto Item);

public class GetMenuItemAnalyticsByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemAnalytics");

        group.MapGet("/restaurants/{restaurantId}/analytics/menu-items/{id:int}", async (
            Guid restaurantId,
            int id,
            ISender sender) =>
        {
            var query = new GetMenuItemAnalyticsByIdQuery(id, restaurantId);
            var result = await sender.Send(query);
            return Results.Ok(new GetMenuItemAnalyticsByIdResponse(result.Item));
        })
        .WithDescription("Gets a single menu item analytics record by ID.")
        .WithName("GetMenuItemAnalyticsById")
        .Produces<GetMenuItemAnalyticsByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}