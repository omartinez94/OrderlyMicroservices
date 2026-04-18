namespace Catalog.API.Features.Restaurants.GetRestaurants;

public record GetRestaurantsQuery(int? PageNumber = 1, int? PageSize = 10) : IQuery<GetRestaurantsResult>;

public record GetRestaurantsResult(IEnumerable<Restaurant> Restaurants);

internal class GetRestaurantsQueryHandler(IDocumentSession session) : IQueryHandler<GetRestaurantsQuery, GetRestaurantsResult>
{
    public async Task<GetRestaurantsResult> Handle(GetRestaurantsQuery query, CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber ?? 1;
        var pageSize = query.PageSize ?? 10;
        if (pageSize > 50)
        {
            pageSize = 50;
        }
        
        var restaurants = await session.Query<Restaurant>()
            .ToPagedListAsync(pageNumber, pageSize, cancellationToken);

        return new GetRestaurantsResult(restaurants);
    }
}
