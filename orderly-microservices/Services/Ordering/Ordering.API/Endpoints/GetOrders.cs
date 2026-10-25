using BuildingBlocks.Exceptions;
using Microsoft.AspNetCore.Mvc;
using NodaTime.Text;
using Ordering.Application.Orders.Queries.GetOrders;

namespace Ordering.API.Endpoints;

public record GetOrdersResponse(PaginatedResult<OrderDto> Orders);

public class GetOrders : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        group.MapGet("/orders", async (
            [AsParameters] PaginationRequest request,
            [FromQuery(Name = "status")] string[]? status,
            [FromQuery] string? orderType,
            [FromQuery] string? search,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] bool? activeOnly,
            [FromQuery] string? sort,
            [FromQuery] string? dir,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<OrderStatus>? parsedStatuses = ParseStatusesOrNull(status);
            OrderType? parsedOrderType = ParseOrderTypeOrNull(orderType);
            Instant? fromInstant = ParseInstantOrNull(from, nameof(from));
            Instant? toInstant = ParseInstantOrNull(to, nameof(to));

            GetOrdersQuery query = new(
                PaginationRequest: request,
                Status: parsedStatuses,
                OrderType: parsedOrderType,
                Search: search,
                From: fromInstant,
                To: toInstant,
                ActiveOnly: activeOnly ?? false,
                Sort: sort,
                Dir: dir);

            GetOrdersResult result = await sender.Send(query, cancellationToken);
            GetOrdersResponse response = result.Adapt<GetOrdersResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("orders:view_own")
        .WithDescription("Gets a paginated list of orders with optional status, type, date, and search filters.")
        .WithName("GetOrders")
        .Produces<GetOrdersResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    internal static IReadOnlyList<OrderStatus>? ParseStatusesOrNull(string[]? rawStatuses)
    {
        if (rawStatuses is null || rawStatuses.Length == 0)
        {
            return null;
        }

        List<OrderStatus> results = [];
        foreach (string rawEntry in rawStatuses)
        {
            if (string.IsNullOrWhiteSpace(rawEntry))
            {
                continue;
            }

            string[] tokens = rawEntry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string token in tokens)
            {
                if (!Enum.TryParse<OrderStatus>(token, ignoreCase: true, out OrderStatus parsed) ||
                    !Enum.IsDefined(parsed))
                {
                    throw new BadRequestException($"Invalid status filter value '{token}'.");
                }

                results.Add(parsed);
            }
        }

        return results.Count > 0 ? results : null;
    }

    internal static OrderType? ParseOrderTypeOrNull(string? rawOrderType)
    {
        if (string.IsNullOrWhiteSpace(rawOrderType))
        {
            return null;
        }

        if (!Enum.TryParse<OrderType>(rawOrderType.Trim(), ignoreCase: true, out OrderType parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new BadRequestException($"Invalid orderType filter value '{rawOrderType}'.");
        }

        return parsed;
    }

    internal static Instant? ParseInstantOrNull(string? raw, string paramName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        ParseResult<Instant> parseResult = InstantPattern.ExtendedIso.Parse(raw.Trim());
        if (!parseResult.Success)
        {
            throw new BadRequestException(
                $"'{paramName}' must be an ISO-8601 instant (e.g. 2026-10-24T12:00:00Z). Got: '{raw}'.");
        }

        return parseResult.Value;
    }
}
