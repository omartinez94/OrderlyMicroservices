namespace Ordering.Application.Orders.Queries.GetLiveOrders;

public record GetLiveOrdersQuery(Instant? Since = null) : IQuery<GetLiveOrdersResult>;

public record GetLiveOrdersResult(
    IReadOnlyList<OrderSummaryDto> Orders,
    Instant ServerTime,
    bool Truncated,
    bool ResyncRequired);
