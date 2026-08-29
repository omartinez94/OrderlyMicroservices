using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.GetMenuCategoryById;

public record GetMenuCategoryByIdQuery(int Id) : IQuery<GetMenuCategoryByIdResult>;

public record GetMenuCategoryByIdResult(MenuCategoryDto MenuCategory);

internal class GetMenuCategoryByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuCategoryByIdQuery, GetMenuCategoryByIdResult>
{
    public async Task<GetMenuCategoryByIdResult> Handle(GetMenuCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        IQueryable<MenuCategory>? category = dbContext.MenuCategories
            .AsNoTracking()
            .AsQueryable();

        MenuCategory? firstCat = await category.FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken);

        return category is null
            ? throw new MenuCategoryNotFoundException(query.Id)
            : new GetMenuCategoryByIdResult(category.Adapt<MenuCategoryDto>());
    }
}
