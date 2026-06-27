using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Tables.GetTableById;

public record GetTableByIdQuery(Guid Id) : IQuery<GetTableByIdResult>;

public record GetTableByIdResult(Table Table);

internal class GetTableByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetTableByIdQuery, GetTableByIdResult>
{
    public async Task<GetTableByIdResult> Handle(GetTableByIdQuery query, CancellationToken cancellationToken)
    {
        var table = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            dbContext.Tables.AsNoTracking(),
            t => t.Id == query.Id,
            cancellationToken)
            ?? throw new TableNotFoundException(query.Id);

        return new GetTableByIdResult(table);
    }
}
