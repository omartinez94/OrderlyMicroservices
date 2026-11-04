using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuSubCategories.GetMenuSubCategoryById;

public record GetMenuSubCategoryByIdQuery(int Id) : IQuery<GetMenuSubCategoryByIdResult>;

public record GetMenuSubCategoryByIdResult(MenuSubCategoryDto SubCategory);

public class GetMenuSubCategoryByIdQueryValidator : AbstractValidator<GetMenuSubCategoryByIdQuery>
{
    public GetMenuSubCategoryByIdQueryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class GetMenuSubCategoryByIdQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuSubCategoryByIdQuery, GetMenuSubCategoryByIdResult>
{
    public async Task<GetMenuSubCategoryByIdResult> Handle(GetMenuSubCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        MenuSubCategory? subCategory = await dbContext.MenuSubCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(msc => msc.Id == query.Id, cancellationToken);

        if (subCategory is null)
        {
            throw new MenuSubCategoryNotFoundException(query.Id);
        }

        MenuSubCategoryDto? dto = subCategory.Adapt<MenuSubCategoryDto>();

        Guid? categoryRestaurantId = await dbContext.MenuCategories
            .AsNoTracking()
            .Where(c => c.Id == subCategory.CategoryId)
            .Select(c => (Guid?)c.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (categoryRestaurantId is not null)
        {
            currentUser.EnsureTenantAccess(categoryRestaurantId.Value);
        }

        return new GetMenuSubCategoryByIdResult(dto);
    }
}
