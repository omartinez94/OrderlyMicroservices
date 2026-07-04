namespace Kitchen.API.Application.KitchenTickets.Queries;

/// <summary>
/// Returns the kitchen queue (tickets in <c>New</c> or <c>InProgress</c>),
/// optionally filtered by restaurant and station. Used by the
/// <c>GET /api/v1/kitchen/queue</c> endpoint.
/// </summary>
public record GetKitchenQueueQuery(
    Guid? RestaurantId,
    Guid? StationId,
    int Page = 1,
    int PageSize = 50) : IQuery<PaginatedResult<KitchenTicketDto>>;

public class GetKitchenQueueHandler(
    IKitchenTicketRepository repository)
    : IQueryHandler<GetKitchenQueueQuery, PaginatedResult<KitchenTicketDto>>
{
    public async Task<PaginatedResult<KitchenTicketDto>> Handle(
        GetKitchenQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Page < 1) query = query with { Page = 1 };
        if (query.PageSize < 1) query = query with { PageSize = 50 };
        if (query.PageSize > 200) query = query with { PageSize = 200 };

        int skip = (query.Page - 1) * query.PageSize;
        IReadOnlyList<KitchenTicket> rows = await repository.GetQueueAsync(
            query.RestaurantId,
            query.StationId,
            skip,
            query.PageSize,
            cancellationToken);

        IReadOnlyList<KitchenTicketDto> dtos = rows.Select(t => t.ToDto()).ToList();

        return new PaginatedResult<KitchenTicketDto>(
            query.Page,
            query.PageSize,
            dtos.Count,
            dtos);
    }
}