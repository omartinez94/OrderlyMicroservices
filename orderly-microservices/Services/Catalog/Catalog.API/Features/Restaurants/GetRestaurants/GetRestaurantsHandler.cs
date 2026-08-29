using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Restaurants.GetRestaurants;

public record GetRestaurantsQuery(int? PageNumber = 1, int? PageSize = 10) : IQuery<GetRestaurantsResult>;

public record GetRestaurantsResult(IEnumerable<Restaurant> Restaurants);

internal class GetRestaurantsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetRestaurantsQuery, GetRestaurantsResult>
{
    public async Task<GetRestaurantsResult> Handle(GetRestaurantsQuery query, CancellationToken cancellationToken)
    {
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = Math.Clamp(query.PageSize ?? 10, 1, 50);

        List<Restaurant>? restaurants = await EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.Restaurants
                .AsNoTracking()
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize),
            cancellationToken);

        return new GetRestaurantsResult(restaurants);
    }
}
