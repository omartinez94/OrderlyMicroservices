using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemIngredients.GetMenuItemIngredients;

public record GetMenuItemIngredientsQuery(Guid MenuItemId) : IQuery<GetMenuItemIngredientsResult>;

public record GetMenuItemIngredientsResult(IEnumerable<MenuItemIngredientDto> Ingredients);

public class GetMenuItemIngredientsQueryValidator : AbstractValidator<GetMenuItemIngredientsQuery>
{
    public GetMenuItemIngredientsQueryValidator()
    {
        RuleFor(x => x.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
    }
}

internal class GetMenuItemIngredientsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuItemIngredientsQuery, GetMenuItemIngredientsResult>
{
    public async Task<GetMenuItemIngredientsResult> Handle(GetMenuItemIngredientsQuery query, CancellationToken cancellationToken)
    {
        List<MenuItemIngredient>? ingredients = await dbContext.MenuItemIngredients
            .AsNoTracking()
            .Where(x => x.MenuItemId == query.MenuItemId)
            .ToListAsync(cancellationToken);

        IEnumerable<MenuItemIngredientDto>? dtos = ingredients.Adapt<IEnumerable<MenuItemIngredientDto>>();

        return new GetMenuItemIngredientsResult(dtos);
    }
}
