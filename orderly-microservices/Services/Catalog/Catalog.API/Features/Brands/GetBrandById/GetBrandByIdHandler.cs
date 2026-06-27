using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Brands.GetBrandById;

public record GetBrandByIdQuery(Guid Id) : IQuery<GetBrandByIdResult>;

public record GetBrandByIdResult(Brand Brand);

internal class GetBrandByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetBrandByIdQuery, GetBrandByIdResult>
{
    public async Task<GetBrandByIdResult> Handle(GetBrandByIdQuery query, CancellationToken cancellationToken)
    {
        var brand = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            dbContext.Brands.AsNoTracking(),
            b => b.Id == query.Id,
            cancellationToken)
            ?? throw new BrandNotFoundException(query.Id);

        return new GetBrandByIdResult(brand);
    }
}
