namespace Catalog.API.Restaurants.GetRestaurants;

public record GetRestaurantsQuery(int? PageNumber = 1, int? PageSize = 10) : IQuery<GetRestaurantsResult>;

public record GetRestaurantsResult(IEnumerable<Restaurant> Restaurants);

internal class GetRestaurantsQueryHandler(IDocumentSession session, ILogger<GetRestaurantsQueryHandler> logger) : IQueryHandler<GetRestaurantsQuery, GetRestaurantsResult>
{
    public async Task<GetRestaurantsResult> Handle(GetRestaurantsQuery query, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Getting restaurants with PageNumber: {PageNumber} and PageSize: {PageSize}", query.PageNumber, query.PageSize);
        }

        var pageNumber = query.PageNumber ?? 1;
        var pageSize = query.PageSize ?? 10;
        if (pageSize > 50)
        {
            pageSize = 50;
        }
        
        var restaurants = await session.Query<Restaurant>()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GetRestaurantsResult(restaurants);
    }
}
