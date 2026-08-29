using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Tables.GetTables;

public record GetTablesQuery(Guid RestaurantId, TableStatus? Status = null, int? PageNumber = 1, int? PageSize = 10) : IQuery<GetTablesResult>;

public record GetTablesResult(IEnumerable<Table> Tables, int TotalCount);

internal class GetTablesQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetTablesQuery, GetTablesResult>
{
    public async Task<GetTablesResult> Handle(GetTablesQuery query, CancellationToken cancellationToken)
    {
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = Math.Clamp(query.PageSize ?? 10, 1, 50);

        IQueryable<Table>? baseQuery = dbContext.Tables
            .AsNoTracking()
            .Where(t => t.RestaurantId == query.RestaurantId);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.Status == query.Status.Value);
        }

        int totalCount = await EntityFrameworkQueryableExtensions.CountAsync(baseQuery, cancellationToken);

        List<Table>? tables = await EntityFrameworkQueryableExtensions.ToListAsync(
            baseQuery
                .OrderBy(t => t.TableNumber)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize),
            cancellationToken);

        return new GetTablesResult(tables, totalCount);
    }
}
