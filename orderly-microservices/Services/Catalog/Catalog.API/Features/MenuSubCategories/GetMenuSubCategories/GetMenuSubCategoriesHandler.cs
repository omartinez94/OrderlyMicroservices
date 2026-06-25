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

internal class GetMenuSubCategoriesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuSubCategoriesQuery, GetMenuSubCategoriesResult>
{
    public async Task<GetMenuSubCategoriesResult> Handle(GetMenuSubCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categoryExists = await dbContext.MenuCategories.AnyAsync(c => c.Id == query.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            throw new MenuCategoryNotFoundException(query.CategoryId);
        }

        var subCategories = await dbContext.MenuSubCategories
            .AsNoTracking()
            .Where(msc => msc.CategoryId == query.CategoryId)
            .OrderBy(msc => msc.DisplayOrder)
            .ToListAsync(cancellationToken);

        var dtos = subCategories.Adapt<IEnumerable<MenuSubCategoryDto>>();

        return new GetMenuSubCategoriesResult(dtos);
    }
}
