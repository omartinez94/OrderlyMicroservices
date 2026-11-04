using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuSubCategories.GetMenuSubCategories;

public record GetMenuSubCategoriesQuery(int CategoryId) : IQuery<GetMenuSubCategoriesResult>;

public record GetMenuSubCategoriesResult(IEnumerable<MenuSubCategoryDto> SubCategories);

public class GetMenuSubCategoriesQueryValidator : AbstractValidator<GetMenuSubCategoriesQuery>
{
    public GetMenuSubCategoriesQueryValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("CategoryId must be greater than 0");
    }
}

internal class GetMenuSubCategoriesQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuSubCategoriesQuery, GetMenuSubCategoriesResult>
{
    public async Task<GetMenuSubCategoriesResult> Handle(GetMenuSubCategoriesQuery query, CancellationToken cancellationToken)
    {
        Guid? categoryRestaurantId = await dbContext.MenuCategories
            .AsNoTracking()
            .Where(c => c.Id == query.CategoryId)
            .Select(c => (Guid?)c.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        bool categoryExists = categoryRestaurantId is not null;
        if (!categoryExists)
        {
            throw new MenuCategoryNotFoundException(query.CategoryId);
        }

        currentUser.EnsureTenantAccess(categoryRestaurantId!.Value);

        List<MenuSubCategory>? subCategories = await dbContext.MenuSubCategories
            .AsNoTracking()
            .Where(msc => msc.CategoryId == query.CategoryId)
            .OrderBy(msc => msc.DisplayOrder)
            .ToListAsync(cancellationToken);

        IEnumerable<MenuSubCategoryDto>? dtos = subCategories.Adapt<IEnumerable<MenuSubCategoryDto>>();

        return new GetMenuSubCategoriesResult(dtos);
    }
}
