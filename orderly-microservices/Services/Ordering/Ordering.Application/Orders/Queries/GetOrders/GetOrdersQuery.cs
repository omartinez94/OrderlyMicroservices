namespace Ordering.Application.Orders.Queries.GetOrders;

public record GetOrdersQuery(
    PaginationRequest PaginationRequest,
    IReadOnlyList<OrderStatus>? Status = null,
    OrderType? OrderType = null,
    string? Search = null,
    Instant? From = null,
    Instant? To = null,
    bool ActiveOnly = false,
    string? Sort = null,
    string? Dir = null) : IQuery<GetOrdersResult>;

public record GetOrdersResult(PaginatedResult<OrderDto> Orders);

public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "orderNumber",
        "status",
        "total",
        "totalAmount",
    };

    private static readonly HashSet<string> AllowedSortDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc",
    };

    public GetOrdersQueryValidator()
    {
        RuleFor(x => x.PaginationRequest.PageIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("PageIndex must be greater than or equal to 0.");

        RuleFor(x => x.PaginationRequest.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Search)
            .MaximumLength(64)
            .WithMessage("Search must not exceed 64 characters.");

        RuleFor(x => x)
            .Must(HasValidDateRange)
            .WithMessage("From must be less than or equal to To.");

        RuleFor(x => x.Sort)
            .Must(BeValidSortField)
            .WithMessage("Sort must be one of: createdAt, orderNumber, status, total, totalAmount.");

        RuleFor(x => x.Dir)
            .Must(BeValidSortDirection)
            .WithMessage("Dir must be 'asc' or 'desc'.");
    }

    private static bool HasValidDateRange(GetOrdersQuery query)
    {
        if (!query.From.HasValue || !query.To.HasValue)
        {
            return true;
        }

        return query.From.Value <= query.To.Value;
    }

    private static bool BeValidSortField(string? sort) =>
        string.IsNullOrWhiteSpace(sort) || AllowedSortFields.Contains(sort.Trim());

    private static bool BeValidSortDirection(string? dir) =>
        string.IsNullOrWhiteSpace(dir) || AllowedSortDirections.Contains(dir.Trim());
}
