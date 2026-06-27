using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.PriceHistories.GetPriceHistory;

public record GetPriceHistoryQuery(
    Guid RestaurantId,
    Guid? MenuItemId,
    PriceType? PriceType,
    Instant? From,
    Instant? To) : IQuery<GetPriceHistoryResult>;

public record GetPriceHistoryResult(IEnumerable<PriceHistoryDto> PriceHistories);

public class GetPriceHistoryQueryValidator : AbstractValidator<GetPriceHistoryQuery>
{
    public GetPriceHistoryQueryValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");

        RuleFor(x => x.From)
            .LessThanOrEqualTo(x => x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From date must be before or equal to To date");
    }
}

internal class GetPriceHistoryQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetPriceHistoryQuery, GetPriceHistoryResult>
{
    public async Task<GetPriceHistoryResult> Handle(GetPriceHistoryQuery query, CancellationToken cancellationToken)
    {
        var dbQuery = dbContext.PriceHistories
            .AsNoTracking()
            .Where(x => x.RestaurantId == query.RestaurantId);

        if (query.MenuItemId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.MenuItemId == query.MenuItemId.Value);
        }

        if (query.PriceType.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.PriceType == query.PriceType.Value);
        }

        if (query.From.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.EffectiveDate >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.EffectiveDate <= query.To.Value);
        }

        dbQuery = dbQuery.OrderByDescending(x => x.EffectiveDate);

        var priceHistories = await dbQuery.ToListAsync(cancellationToken);

        var dtos = priceHistories.Adapt<IEnumerable<PriceHistoryDto>>();

        return new GetPriceHistoryResult(dtos);
    }
}
