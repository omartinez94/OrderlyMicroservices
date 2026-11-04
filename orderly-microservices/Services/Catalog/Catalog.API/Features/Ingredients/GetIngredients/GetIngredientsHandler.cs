using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Ingredients.GetIngredients;

public record GetIngredientsQuery(Guid RestaurantId, bool? IsAvailable = null) : IQuery<GetIngredientsResult>;

public record GetIngredientsResult(IEnumerable<Ingredient> Ingredients);

internal class GetIngredientsQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetIngredientsQuery, GetIngredientsResult>
{
    public async Task<GetIngredientsResult> Handle(GetIngredientsQuery query, CancellationToken cancellationToken)
    {
        currentUser.EnsureTenantAccess(query.RestaurantId);

        IQueryable<Ingredient>? ingredientsQuery = dbContext.Ingredients
            .AsNoTracking()
            .Where(i => i.RestaurantId == query.RestaurantId);

        if (query.IsAvailable.HasValue)
        {
            ingredientsQuery = ingredientsQuery.Where(i => i.IsAvailable == query.IsAvailable.Value);
        }

        List<Ingredient>? ingredients = await ingredientsQuery.ToListAsync(cancellationToken);

        return new GetIngredientsResult(ingredients);
    }
}
