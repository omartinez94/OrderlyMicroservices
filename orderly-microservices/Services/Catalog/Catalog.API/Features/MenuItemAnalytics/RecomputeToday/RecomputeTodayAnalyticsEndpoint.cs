namespace Catalog.API.Features.MenuItemAnalytics.RecomputeToday;

public record RecomputeTodayAnalyticsResponse(int RestaurantsRecomputed, int ItemsTouched);

public class RecomputeTodayAnalyticsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemAnalytics");

        group.MapPost("/restaurants/{restaurantId}/analytics/menu-items/recompute-today", async (
            Guid restaurantId,
            ISender sender) =>
        {
            var result = await sender.Send(new RecomputeTodayAnalyticsCommand(restaurantId));
            return Results.Ok(result.Adapt<RecomputeTodayAnalyticsResponse>());
        })
        .WithDescription("Admin action that recomputes today's MenuItemAnalytics rows for drift repair.")
        .WithName("RecomputeTodayAnalytics")
        .Produces<RecomputeTodayAnalyticsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}