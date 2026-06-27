using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemAnalytics.GetMenuItemAnalyticsById;

public record GetMenuItemAnalyticsByIdQuery(int Id, Guid RestaurantId)
    : IQuery<GetMenuItemAnalyticsByIdResult>;

public record GetMenuItemAnalyticsByIdResult(MenuItemAnalyticsDto Item);

internal class GetMenuItemAnalyticsByIdQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetMenuItemAnalyticsByIdQuery, GetMenuItemAnalyticsByIdResult>
{
    public async Task<GetMenuItemAnalyticsByIdResult> Handle(
        GetMenuItemAnalyticsByIdQuery query,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.MenuItemAnalytics
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.RestaurantId == query.RestaurantId, cancellationToken)
            ?? throw new MenuItemAnalyticsNotFoundException(query.Id);

        return new GetMenuItemAnalyticsByIdResult(item.Adapt<MenuItemAnalyticsDto>());
    }
}