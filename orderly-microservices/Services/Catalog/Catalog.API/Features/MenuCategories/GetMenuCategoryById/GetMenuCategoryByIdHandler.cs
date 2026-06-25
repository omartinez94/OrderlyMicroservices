namespace Catalog.API.Features.MenuCategories.GetMenuCategoryById;

public record GetMenuCategoryByIdQuery(int Id) : IQuery<GetMenuCategoryByIdResult>;

public record GetMenuCategoryByIdResult(MenuCategoryDto MenuCategory);

internal class GetMenuCategoryByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuCategoryByIdQuery, GetMenuCategoryByIdResult>
{
    public async Task<GetMenuCategoryByIdResult> Handle(GetMenuCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        var category = dbContext.MenuCategories
            .AsNoTracking()
            .AsQueryable();

        var firstCat = await category.FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken);

        return category == null
            ? throw new MenuCategoryNotFoundException(query.Id)
            : new GetMenuCategoryByIdResult(category.Adapt<MenuCategoryDto>());
    }
}
