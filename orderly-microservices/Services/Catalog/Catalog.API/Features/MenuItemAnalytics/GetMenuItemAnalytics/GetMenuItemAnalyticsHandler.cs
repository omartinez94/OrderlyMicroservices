using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalytics;

public record GetMenuItemAnalyticsQuery(
    Guid RestaurantId,
    Guid? MenuItemId,
    LocalDate? From,
    LocalDate? To,
    int PageIndex = 0,
    int PageSize = 20) : IQuery<GetMenuItemAnalyticsResult>;

public record GetMenuItemAnalyticsResult(
    IEnumerable<MenuItemAnalyticsDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public class GetMenuItemAnalyticsQueryValidator : AbstractValidator<GetMenuItemAnalyticsQuery>
{
    public GetMenuItemAnalyticsQueryValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");

        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(0).WithMessage("PageIndex must be >= 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100");

        RuleFor(x => x.From)
            .LessThanOrEqualTo(x => x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From date must be before or equal to To date");
    }
}

internal class GetMenuItemAnalyticsQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetMenuItemAnalyticsQuery, GetMenuItemAnalyticsResult>
{
    public async Task<GetMenuItemAnalyticsResult> Handle(
        GetMenuItemAnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var dbQuery = dbContext.MenuItemAnalytics
            .AsNoTracking()
            .Where(x => x.RestaurantId == query.RestaurantId);

        if (query.MenuItemId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.MenuItemId == query.MenuItemId.Value);
        }

        if (query.From.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.AnalysisDate >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.AnalysisDate <= query.To.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(x => x.AnalysisDate)
            .Skip(query.PageIndex * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<IEnumerable<MenuItemAnalyticsDto>>();

        return new GetMenuItemAnalyticsResult(dtos, totalCount, query.PageIndex, query.PageSize);
    }
}