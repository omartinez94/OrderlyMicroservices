namespace Ordering.API.Endpoints.Dashboard;

using BuildingBlocks.Authorization;
using Ordering.Application.Dashboard.Dtos;
using Ordering.Application.Dashboard.Queries.GetRestaurantDashboardLiveSummary;

public class GetRestaurantDashboardLiveSummaryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Dashboard");

        group.MapGet("/restaurants/{restaurantId:guid}/dashboard/live-summary", async (
            Guid restaurantId,
            HttpContext httpContext,
            IConfiguration configuration,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (!configuration.GetValue("FeatureManagement:OrdersLiveView", true))
            {
                return Results.NotFound();
            }

            // Tenant claim check: caller must have access to restaurantId (matching restaurantId, restaurant_ids, or SuperAdmin)
            if (!httpContext.User.HasRestaurantAccess(restaurantId))
            {
                return Results.Forbid();
            }

            bool includeRevenue = httpContext.User.Claims.Any(c =>
                (c.Type == "permissions" || c.Type == "permission") &&
                string.Equals(c.Value, "payments:view_reports", StringComparison.Ordinal));

            GetRestaurantDashboardLiveSummaryQuery query = new(restaurantId, includeRevenue);
            GetRestaurantDashboardLiveSummaryResult result = await sender.Send(query, cancellationToken);

            return Results.Ok(result.Dashboard);
        })
        .RequirePermission("dashboard:view")
        .WithDescription("Gets aggregated live operational summary, pace, and pulse for a restaurant dashboard.")
        .WithName("GetRestaurantDashboardLiveSummary")
        .Produces<RestaurantOperationsDashboardDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
