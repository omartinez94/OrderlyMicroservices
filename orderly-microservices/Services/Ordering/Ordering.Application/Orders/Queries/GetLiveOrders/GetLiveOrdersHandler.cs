namespace Ordering.Application.Orders.Queries.GetLiveOrders;

public class GetLiveOrdersHandler(
    IApplicationDbContext dbContext,
    IClock clock)
    : IQueryHandler<GetLiveOrdersQuery, GetLiveOrdersResult>
{
    public const int LiveOrdersHardCap = 300;
    public const int RecentlyClosedWindowMinutes = 15;

    private static readonly Instant SqlMinInstant = NodaConstants.BclEpoch;

    private static readonly OrderStatus[] ActiveStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Preparing,
        OrderStatus.Ready,
        OrderStatus.OnHold,
        OrderStatus.CancelRequested,
    ];

    private static readonly OrderStatus[] ClosedStatuses =
    [
        OrderStatus.Completed,
        OrderStatus.Delivered,
        OrderStatus.Cancelled,
    ];

    public async Task<GetLiveOrdersResult> Handle(
        GetLiveOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Instant serverTime = clock.GetCurrentInstant();
        Duration closedWindow = Duration.FromMinutes(RecentlyClosedWindowMinutes);

        if (query.Since.HasValue)
        {
            Instant since = query.Since.Value;
            if ((serverTime - since) <= closedWindow)
            {
                return await ExecuteSinceQueryAsync(since, serverTime, cancellationToken);
            }

            GetLiveOrdersResult fullSnapshot = await ExecuteLiveSnapshotAsync(
                serverTime,
                closedWindow,
                cancellationToken);

            return fullSnapshot with { ResyncRequired = true };
        }

        return await ExecuteLiveSnapshotAsync(serverTime, closedWindow, cancellationToken);
    }

    private async Task<GetLiveOrdersResult> ExecuteSinceQueryAsync(
        Instant since,
        Instant serverTime,
        CancellationToken cancellationToken)
    {
        List<Order> rawOrders = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.Status != OrderStatus.Ordering)
            .Where(o =>
                (o.LastModified.HasValue && o.LastModified >= since) ||
                (!o.LastModified.HasValue && o.CreatedAt.HasValue && o.CreatedAt >= since) ||
                (!o.LastModified.HasValue && !o.CreatedAt.HasValue && o.ConfirmedAt.HasValue && o.ConfirmedAt >= since))
            .OrderByDescending(o => o.CreatedAt ?? SqlMinInstant)
            .Take(LiveOrdersHardCap + 1)
            .ToListAsync(cancellationToken);

        bool exceeded = rawOrders.Count > LiveOrdersHardCap;
        IReadOnlyList<OrderSummaryDto> dtos =
        [
            .. rawOrders
                .Take(LiveOrdersHardCap)
                .Select(o => o.ToOrderSummaryDto())
        ];

        return new GetLiveOrdersResult(
            Orders: dtos,
            ServerTime: serverTime,
            Truncated: exceeded,
            ResyncRequired: exceeded);
    }

    private async Task<GetLiveOrdersResult> ExecuteLiveSnapshotAsync(
        Instant serverTime,
        Duration closedWindow,
        CancellationToken cancellationToken)
    {
        Instant closedCutoff = serverTime - closedWindow;

        List<Order> rawOrders = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.Status != OrderStatus.Ordering)
            .Where(o =>
                ActiveStatuses.Contains(o.Status) ||
                (ClosedStatuses.Contains(o.Status) &&
                    ((o.CompletedAt.HasValue && o.CompletedAt >= closedCutoff) ||
                     (o.DeliveredAt.HasValue && o.DeliveredAt >= closedCutoff) ||
                     (o.CancelledAt.HasValue && o.CancelledAt >= closedCutoff) ||
                     (o.LastModified.HasValue && o.LastModified >= closedCutoff) ||
                     (o.CreatedAt.HasValue && o.CreatedAt >= closedCutoff))))
            .OrderByDescending(o => o.CreatedAt ?? SqlMinInstant)
            .Take(LiveOrdersHardCap + 1)
            .ToListAsync(cancellationToken);

        bool truncated = rawOrders.Count > LiveOrdersHardCap;
        IReadOnlyList<OrderSummaryDto> dtos =
        [
            .. rawOrders
                .Take(LiveOrdersHardCap)
                .Select(o => o.ToOrderSummaryDto())
        ];

        return new GetLiveOrdersResult(
            Orders: dtos,
            ServerTime: serverTime,
            Truncated: truncated,
            ResyncRequired: false);
    }
}
