using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Brands.GetBrandById;

public record GetBrandByIdQuery(string Identifier) : IQuery<GetBrandByIdResult>
{
    public GetBrandByIdQuery(Guid id) : this(id.ToString()) { }

    public Guid Id => Guid.TryParse(Identifier, out Guid guid) ? guid : Guid.Empty;
}

public record GetBrandByIdResult(Brand Brand);

internal class GetBrandByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetBrandByIdQuery, GetBrandByIdResult>
{
    public async Task<GetBrandByIdResult> Handle(GetBrandByIdQuery query, CancellationToken cancellationToken)
    {
        Brand? brand;

        if (Guid.TryParse(query.Identifier, out Guid guidId))
        {
            brand = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Brands.AsNoTracking(),
                b => b.Id == guidId,
                cancellationToken);
        }
        else
        {
            brand = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Brands.AsNoTracking(),
                b => b.ShortId == query.Identifier,
                cancellationToken);
        }

        if (brand is null)
        {
            throw new BrandNotFoundException(query.Identifier);
        }

        return new GetBrandByIdResult(brand);
    }
}
