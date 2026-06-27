using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.IngredientAlternatives.GetIngredientAlternatives;

public record GetIngredientAlternativesQuery(Guid RestaurantId) : IQuery<GetIngredientAlternativesResult>;

public record GetIngredientAlternativesResult(IEnumerable<IngredientAlternativeDto> IngredientAlternatives);

public class GetIngredientAlternativesQueryValidator : AbstractValidator<GetIngredientAlternativesQuery>
{
    public GetIngredientAlternativesQueryValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");
    }
}

internal class GetIngredientAlternativesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetIngredientAlternativesQuery, GetIngredientAlternativesResult>
{
    public async Task<GetIngredientAlternativesResult> Handle(GetIngredientAlternativesQuery query, CancellationToken cancellationToken)
    {
        var alternatives = await dbContext.IngredientAlternatives
            .Where(x => x.RestaurantId == query.RestaurantId)
            .ToListAsync(cancellationToken);

        var dtos = alternatives.Adapt<IEnumerable<IngredientAlternativeDto>>();

        return new GetIngredientAlternativesResult(dtos);
    }
}
