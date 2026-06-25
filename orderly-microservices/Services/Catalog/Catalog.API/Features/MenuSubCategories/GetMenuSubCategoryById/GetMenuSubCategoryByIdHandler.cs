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

internal class GetMenuSubCategoryByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuSubCategoryByIdQuery, GetMenuSubCategoryByIdResult>
{
    public async Task<GetMenuSubCategoryByIdResult> Handle(GetMenuSubCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        var subCategory = await dbContext.MenuSubCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(msc => msc.Id == query.Id, cancellationToken);

        if (subCategory is null)
        {
            throw new MenuSubCategoryNotFoundException(query.Id);
        }

        var dto = subCategory.Adapt<MenuSubCategoryDto>();

        return new GetMenuSubCategoryByIdResult(dto);
    }
}
