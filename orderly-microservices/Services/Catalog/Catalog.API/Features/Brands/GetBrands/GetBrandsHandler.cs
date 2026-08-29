using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Brands.GetBrands;

public record GetBrandsQuery(int? PageNumber = 1, int? PageSize = 10) : IQuery<GetBrandsResult>;

public record GetBrandsResult(IEnumerable<Brand> Brands);

internal class GetBrandsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetBrandsQuery, GetBrandsResult>
{
    public async Task<GetBrandsResult> Handle(GetBrandsQuery query, CancellationToken cancellationToken)
    {
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 10;
        if (pageSize > 50)
        {
            pageSize = 50;
        }

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
