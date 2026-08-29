using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Brands.GetBrands;

public record GetBrandsQuery(int? PageNumber = 1, int? PageSize = 10) : IQuery<GetBrandsResult>;

public record GetBrandsResult(IEnumerable<Brand> Brands);

internal class GetBrandsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetBrandsQuery, GetBrandsResult>
{
    public async Task<GetBrandsResult> Handle(GetBrandsQuery query, CancellationToken cancellationToken)
    {
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = Math.Clamp(query.PageSize ?? 10, 1, 50);

        List<Brand>? brands = await EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.Brands
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize),
            cancellationToken);

        return new GetBrandsResult(brands);
    }
}
