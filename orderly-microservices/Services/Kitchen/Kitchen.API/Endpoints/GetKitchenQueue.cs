namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>GET /api/v1/kitchen/queue</c> — paginated list of tickets in
/// <c>New</c> or <c>InProgress</c> status, filterable by restaurant and
/// station. Returns a <c>PaginatedResult&lt;KitchenTicketDto&gt;</c>.
/// </summary>
public class GetKitchenQueue : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/kitchen/queue", async (
            [AsParameters] GetKitchenQueueRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            GetKitchenQueueQuery query = new(
                request.RestaurantId,
                request.StationId,
                request.Page ?? 1,
                request.PageSize ?? 50);
            PaginatedResult<KitchenTicketDto> result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithTags("Kitchen")
        .RequireAuthorization("kitchen:view_orders");
    }

    private record GetKitchenQueueRequest(
        Guid? RestaurantId,
        Guid? StationId,
        int? Page,
        int? PageSize);
}