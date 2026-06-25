using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.GetMenuCategories;

public record GetMenuCategoriesQuery(Guid RestaurantId) : IQuery<GetMenuCategoriesResult>;

public record GetMenuCategoriesResult(IEnumerable<MenuCategoryDto> MenuCategories);

internal class GetMenuCategoriesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuCategoriesQuery, GetMenuCategoriesResult>
{
    public async Task<GetMenuCategoriesResult> Handle(GetMenuCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.MenuCategories
                .AsNoTracking()
                .Where(c => c.RestaurantId == query.RestaurantId)
                .OrderBy(c => c.DisplayOrder),
            cancellationToken);

        var dtos = categories.Adapt<IEnumerable<MenuCategoryDto>>();

        return new GetMenuCategoriesResult(dtos);
    }
}
