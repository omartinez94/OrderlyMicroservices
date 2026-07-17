using BuildingBlocks.Pagination;
using Ordering.Domain.Enums;

namespace Ordering.Application.Orders.Queries.GetOrderActivities;

/// <summary>
/// Standalone paged read of an order's activity feed. Filters by activity
/// type and by an optional half-open date range; pagination is the same
/// <see cref="PaginationRequest"/> the <c>GET /orders</c> endpoint uses.
/// </summary>
/// <param name="OrderId">Guid id of the order whose activities to load.</param>
/// <param name="Type">Optional <see cref="OrderActivityType"/> filter; <c>null</c> = no filter.</param>
/// <param name="From">Optional inclusive lower bound on <c>OccurredAt</c>; <c>null</c> = unbounded.</param>
/// <param name="To">Optional inclusive upper bound on <c>OccurredAt</c>; <c>null</c> = unbounded.</param>
/// <param name="Pagination">Page index + page size. <see cref="PaginatedResult{T}.Count"/> reports the total number of activities that match the filters, pre-Skip/Take.</param>
public record GetOrderActivitiesQuery(
    Guid OrderId,
    OrderActivityType? Type,
    Instant? From,
    Instant? To,
    PaginationRequest Pagination) : IQuery<GetOrderActivitiesResult>;

/// <summary>
/// Handler result. The <see cref="BuildingBlocks.Pagination.PaginatedResult{T}"/>
/// wraps <see cref="OrderActivityDto"/> rows ordered by
/// <c>OccurredAt ASC, Id ASC</c> (deterministic Guid tie-breaker).
/// </summary>
public record GetOrderActivitiesResult(PaginatedResult<OrderActivityDto> Activities);
