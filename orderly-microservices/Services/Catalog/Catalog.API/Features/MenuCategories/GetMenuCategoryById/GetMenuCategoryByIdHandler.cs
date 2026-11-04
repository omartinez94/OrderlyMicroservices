using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuCategories.GetMenuCategoryById;

public record GetMenuCategoryByIdQuery(int Id) : IQuery<GetMenuCategoryByIdResult>;

public record GetMenuCategoryByIdResult(MenuCategoryDto MenuCategory);

internal class GetMenuCategoryByIdQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuCategoryByIdQuery, GetMenuCategoryByIdResult>
{
    public async Task<GetMenuCategoryByIdResult> Handle(GetMenuCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        MenuCategory? category = await dbContext.MenuCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new MenuCategoryNotFoundException(query.Id);

        currentUser.EnsureTenantAccess(category.RestaurantId);

        return new GetMenuCategoryByIdResult(category.Adapt<MenuCategoryDto>());
    }
}
