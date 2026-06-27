using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Ingredients.GetIngredientById;

public record GetIngredientByIdQuery(Guid RestaurantId, int Id) : IQuery<GetIngredientByIdResult>;

public record GetIngredientByIdResult(Ingredient Ingredient);

internal class GetIngredientByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetIngredientByIdQuery, GetIngredientByIdResult>
{
    public async Task<GetIngredientByIdResult> Handle(GetIngredientByIdQuery query, CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.Ingredients
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.RestaurantId == query.RestaurantId && i.Id == query.Id, cancellationToken)
            ?? throw new IngredientNotFoundException(query.Id);

        return new GetIngredientByIdResult(ingredient);
    }
}
