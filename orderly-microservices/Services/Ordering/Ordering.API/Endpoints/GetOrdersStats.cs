using Ordering.Application.Orders.Queries.GetOrdersStats;

namespace Ordering.API.Endpoints;

public class GetOrdersStats : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        group.MapGet("/orders/stats", async (
            HttpContext httpContext,
            IConfiguration configuration,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (!configuration.GetValue("FeatureManagement:OrdersLiveView", true))
            {
                return Results.NotFound();
            }

            bool includeRevenue = httpContext.User.Claims.Any(c =>
                c.Type == "permissions" &&
                string.Equals(c.Value, "payments:view_reports", StringComparison.Ordinal));

            GetOrdersStatsQuery query = new(IncludeRevenue: includeRevenue);
            GetOrdersStatsResult result = await sender.Send(query, cancellationToken);

            return Results.Ok(result.Stats);
        })
        .RequirePermission("orders:view_all")
        .WithDescription("Gets live operational statistics and SLA counts in the restaurant's time zone.")
        .WithName("GetOrdersStats")
        .Produces<OrdersStatsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
