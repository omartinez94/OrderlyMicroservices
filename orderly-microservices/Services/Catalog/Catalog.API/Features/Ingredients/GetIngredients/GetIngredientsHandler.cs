using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Ingredients.GetIngredients;

public record GetIngredientsQuery(Guid RestaurantId, bool? IsAvailable = null) : IQuery<GetIngredientsResult>;

public record GetIngredientsResult(IEnumerable<Ingredient> Ingredients);

internal class GetIngredientsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetIngredientsQuery, GetIngredientsResult>
{
    public async Task<GetIngredientsResult> Handle(GetIngredientsQuery query, CancellationToken cancellationToken)
    {
        var ingredientsQuery = dbContext.Ingredients
            .AsNoTracking()
            .Where(i => i.RestaurantId == query.RestaurantId);

        if (query.IsAvailable.HasValue)
        {
            ingredientsQuery = ingredientsQuery.Where(i => i.IsAvailable == query.IsAvailable.Value);
        }

        var ingredients = await ingredientsQuery.ToListAsync(cancellationToken);

        return new GetIngredientsResult(ingredients);
    }
}
