using NodaTime;
using NodaTime.Text;
using Ordering.Application.Orders.Queries.GetOrderActivities;
using Ordering.Domain.Enums;

namespace Ordering.API.Endpoints;

/// <summary>
/// Standalone paged read of an order's activity feed — separate from
/// <c>GET /api/v1/orders/{id}</c> so callers that only want the activity
/// history don't pay for the full order payload.
/// </summary>
public record GetOrderActivitiesResponse(PaginatedResult<OrderActivityDto> Activities);

public class GetOrderActivities : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Orders");

        group.MapGet("/orders/{id:guid}/activities", async (
            Guid id,
            OrderActivityType? type,
            string? from,
            string? to,
            [AsParameters] PaginationRequest pagination,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var fromInstant = ParseInstantOrNull(from, nameof(from));
            var toInstant = ParseInstantOrNull(to, nameof(to));

            var query = new GetOrderActivitiesQuery(
                OrderId: id,
                Type: type,
                From: fromInstant,
                To: toInstant,
                Pagination: pagination);

            var result = await sender.Send(query, cancellationToken);
            var response = result.Adapt<GetOrderActivitiesResponse>();
            return Results.Ok(response);
        })
        .WithDescription("Gets the paged activity feed for an order.")
        .WithName("GetOrderActivities")
        .RequirePermission("orders:view_own")
        .Produces<GetOrderActivitiesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Parses an ISO-8601 instant from a query-string fragment. Returns
    /// <c>null</c> on absent / empty input; throws <see cref="ArgumentException"/>
    /// (which the global exception handler maps to 400) on malformed input.
    /// </summary>
    private static Instant? ParseInstantOrNull(string? raw, string paramName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parseResult = InstantPattern.ExtendedIso.Parse(raw);
        if (!parseResult.Success)
        {
            throw new ArgumentException(
                $"'{paramName}' must be an ISO-8601 instant (e.g. 2026-07-16T12:00:00Z). Got: '{raw}'.",
                paramName);
        }

        return parseResult.Value;
    }
}
