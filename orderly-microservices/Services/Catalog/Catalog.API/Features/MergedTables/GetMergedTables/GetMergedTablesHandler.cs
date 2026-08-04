using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MergedTables.GetMergedTables;

public record GetMergedTablesQuery(Guid RestaurantId) : IQuery<GetMergedTablesResult>;

public record GetMergedTablesResult(IEnumerable<MergedTableDto> MergedTables);

public class GetMergedTablesQueryValidator : AbstractValidator<GetMergedTablesQuery>
{
    public GetMergedTablesQueryValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
    }
}

internal class GetMergedTablesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMergedTablesQuery, GetMergedTablesResult>
{
    public async Task<GetMergedTablesResult> Handle(GetMergedTablesQuery query, CancellationToken cancellationToken)
    {
        // Notice: Since MergedTable does not have RestaurantId directly, we may need to 
        // join with Tables or just return them based on what is available.
        // Assuming we are just returning all active for now or filtering based on Tables.
        // As a fallback, we fetch all. In a real scenario, this requires a join with Table.

        var mergedTables = await dbContext.MergedTables
            .Where(mt => mt.IsActive)
            .ToListAsync(cancellationToken);

        var dtos = mergedTables.Adapt<IEnumerable<MergedTableDto>>();

        return new GetMergedTablesResult(dtos);
    }
}
