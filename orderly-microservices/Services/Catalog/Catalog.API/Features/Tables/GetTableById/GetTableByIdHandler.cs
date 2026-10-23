using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Tables.GetTableById;

public record GetTableByIdQuery(string Identifier) : IQuery<GetTableByIdResult>
{
    public GetTableByIdQuery(Guid id) : this(id.ToString()) { }

    public Guid Id => Guid.TryParse(Identifier, out Guid guid) ? guid : Guid.Empty;
}

public record GetTableByIdResult(Table Table);

internal class GetTableByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetTableByIdQuery, GetTableByIdResult>
{
    public async Task<GetTableByIdResult> Handle(GetTableByIdQuery query, CancellationToken cancellationToken)
    {
        Table? table;

        if (Guid.TryParse(query.Identifier, out Guid guidId))
        {
            table = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Tables.AsNoTracking(),
                t => t.Id == guidId,
                cancellationToken);
        }
        else
        {
            table = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Tables.AsNoTracking(),
                t => t.ShortId == query.Identifier,
                cancellationToken);
        }

        if (table is null)
        {
            throw new TableNotFoundException(query.Identifier);
        }

        return new GetTableByIdResult(table);
    }
}
