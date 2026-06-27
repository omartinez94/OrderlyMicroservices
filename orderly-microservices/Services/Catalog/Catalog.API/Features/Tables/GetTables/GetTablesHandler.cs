using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Tables.GetTables;

public record GetTablesQuery(Guid RestaurantId, TableStatus? Status = null, int? PageNumber = 1, int? PageSize = 10) : IQuery<GetTablesResult>;

public record GetTablesResult(IEnumerable<Table> Tables, int TotalCount);

internal class GetTablesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetTablesQuery, GetTablesResult>
{
    public async Task<GetTablesResult> Handle(GetTablesQuery query, CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber ?? 1;
        var pageSize = query.PageSize ?? 10;
        if (pageSize > 50) pageSize = 50;

        var baseQuery = dbContext.Tables
            .AsNoTracking()
            .Where(t => t.RestaurantId == query.RestaurantId);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.Status == query.Status.Value);
        }

        var totalCount = await EntityFrameworkQueryableExtensions.CountAsync(baseQuery, cancellationToken);

        var tables = await EntityFrameworkQueryableExtensions.ToListAsync(
            baseQuery
                .OrderBy(t => t.TableNumber)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize),
            cancellationToken);

        return new GetTablesResult(tables, totalCount);
    }
}
