namespace Kitchen.API.Application.KitchenTickets.Queries;

/// <summary>
/// Returns the kitchen queue (tickets in <c>New</c> or <c>InProgress</c>),
/// optionally filtered by restaurant and station. Used by the
/// <c>GET /v1/kitchen/queue</c> endpoint.
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

        // Clamp page-size into [1, 200] with a switch expression on the
        // relational pattern; record's `with` re-applies the immutable
        // copy. Same shape as the catalog list handlers' Math.Clamp.
        query = query with
        {
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize switch
            {
                < 1 => 50,
                > 200 => 200,
                _ => query.PageSize,
            },
        };

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