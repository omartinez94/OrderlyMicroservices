using Microsoft.AspNetCore.Mvc;
using Ordering.Application.Orders.Queries.GetLiveOrders;

namespace Ordering.API.Endpoints;

public record GetLiveOrdersResponse(
    IReadOnlyList<OrderSummaryDto> Orders,
    Instant ServerTime,
    bool Truncated,
    bool ResyncRequired);

public class GetLiveOrders : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        group.MapGet("/orders/live", async (
            [FromQuery] string? since,
            IConfiguration configuration,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (!configuration.GetValue("FeatureManagement:OrdersLiveView", true))
            {
                return Results.NotFound();
            }

            Instant? sinceInstant = GetOrders.ParseInstantOrNull(since, nameof(since));
            GetLiveOrdersQuery query = new(Since: sinceInstant);
            GetLiveOrdersResult result = await sender.Send(query, cancellationToken);
            GetLiveOrdersResponse response = new(
                Orders: result.Orders,
                ServerTime: result.ServerTime,
                Truncated: result.Truncated,
                ResyncRequired: result.ResyncRequired);

            return Results.Ok(response);
        })
        .RequirePermission("orders:view_own")
        .WithDescription("Gets the live set of active and recently-closed orders, stamped with server time.")
        .WithName("GetLiveOrders")
        .Produces<GetLiveOrdersResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
