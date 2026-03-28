namespace Catalog.API.Restaurants.GetRestaurantById;

public record GetRestaurantByIdQuery(Guid Id) : IQuery<GetRestaurantByIdResult>;

public record GetRestaurantByIdResult(Restaurant Restaurant);

internal class GetRestaurantByIdQueryHandler(IDocumentSession session, ILogger<GetRestaurantByIdQueryHandler> logger) : IQueryHandler<GetRestaurantByIdQuery, GetRestaurantByIdResult>
{
    public async Task<GetRestaurantByIdResult> Handle(GetRestaurantByIdQuery query, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Getting restaurant with Id: {Id}", query.Id);
        }

        var restaurant = await session.LoadAsync<Restaurant>(query.Id, cancellationToken) ?? throw new RestaurantNotFoundException(query.Id);

        return new GetRestaurantByIdResult(restaurant);
    }
}
