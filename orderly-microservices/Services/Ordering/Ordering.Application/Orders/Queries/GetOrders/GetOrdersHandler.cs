namespace Ordering.Application.Orders.Queries.GetOrders;

public class GetOrdersHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetOrdersQuery, GetOrdersResult>
{
    private static readonly Instant SqlMinInstant = NodaConstants.BclEpoch;
    private static readonly Instant SqlMaxInstant = Instant.FromUtc(9999, 12, 31, 0, 0, 0);

    private static readonly OrderStatus[] ActiveStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Preparing,
        OrderStatus.Ready,
        OrderStatus.OnHold,
    ];

    public async Task<GetOrdersResult> Handle(GetOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int pageIndex = query.PaginationRequest.PageIndex;
        int pageSize = query.PaginationRequest.PageSize;

        IQueryable<Order> filteredQuery = ApplyFilters(dbContext.Orders.AsNoTracking(), query);

        long totalCount = await filteredQuery.LongCountAsync(cancellationToken);

        IOrderedQueryable<Order> sortedQuery = ApplySorting(filteredQuery, query.Sort, query.Dir);

        List<Order> orders = await sortedQuery
            .Include(o => o.OrderItems)
            .Include(o => o.Activities)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GetOrdersResult(
            new PaginatedResult<OrderDto>(
                pageIndex,
                pageSize,
                totalCount,
                orders.ToOrderDtoList()));
    }

    private static IQueryable<Order> ApplyFilters(IQueryable<Order> source, GetOrdersQuery query)
    {
        IQueryable<Order> result = source;

        if (query.Status is { Count: > 0 })
        {
            OrderStatus[] statuses = [.. query.Status];
            result = result.Where(o => statuses.Contains(o.Status));
        }

        if (query.OrderType.HasValue)
        {
            OrderType orderType = query.OrderType.Value;
            result = result.Where(o => o.OrderType == orderType);
        }

        if (query.ActiveOnly)
        {
            result = result.Where(o => ActiveStatuses.Contains(o.Status));
        }

        result = ApplyDateAndSearchFilters(result, query);

        return result;
    }

    private static IQueryable<Order> ApplyDateAndSearchFilters(IQueryable<Order> source, GetOrdersQuery query)
    {
        IQueryable<Order> result = source;

        if (query.From.HasValue)
        {
            Instant from = query.From.Value;
            result = result.Where(o => o.CreatedAt >= from);
        }

        if (query.To.HasValue)
        {
            Instant to = query.To.Value;
            result = result.Where(o => o.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            OrderNumber termOrderNumber = OrderNumber.Of(term);
            result = result.Where(o =>
                o.ShortId.StartsWith(term) ||
                o.OrderNumber == termOrderNumber ||
                ((string)o.OrderNumber).StartsWith(term) ||
                o.OrderItems.Any(i => i.MenuItemName.Contains(term)));
        }

        return result;
    }

    private static IOrderedQueryable<Order> ApplySorting(
        IQueryable<Order> source,
        string? sort,
        string? dir)
    {
        string normalizedSort = string.IsNullOrWhiteSpace(sort)
            ? "createdat"
            : sort.Trim().ToLowerInvariant();

        bool ascending = string.Equals(dir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSort switch
        {
            "ordernumber" => ascending
                ? source.OrderBy(o => o.OrderNumber)
                : source.OrderByDescending(o => o.OrderNumber),
            "status" => ascending
                ? source.OrderBy(o => o.Status)
                : source.OrderByDescending(o => o.Status),
            "total" or "totalamount" => ascending
                ? source.OrderBy(o => o.TotalAmount)
                : source.OrderByDescending(o => o.TotalAmount),
            _ => ascending
                ? source.OrderBy(o => o.CreatedAt ?? SqlMaxInstant)
                : source.OrderByDescending(o => o.CreatedAt ?? SqlMinInstant),
        };
    }
}
