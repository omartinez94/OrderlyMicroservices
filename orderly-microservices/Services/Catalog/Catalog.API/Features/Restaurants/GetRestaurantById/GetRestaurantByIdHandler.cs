namespace Catalog.API.Features.Restaurants.GetRestaurantById;

public record GetRestaurantByIdQuery(Guid Id) : IQuery<GetRestaurantByIdResult>;

public record GetRestaurantByIdResult(Restaurant Restaurant);

internal class GetRestaurantByIdQueryHandler(IDocumentSession session) : IQueryHandler<GetRestaurantByIdQuery, GetRestaurantByIdResult>
{
    public async Task<GetRestaurantByIdResult> Handle(GetRestaurantByIdQuery query, CancellationToken cancellationToken)
    {
        var restaurant = await session.LoadAsync<Restaurant>(query.Id, cancellationToken) ?? throw new RestaurantNotFoundException(query.Id);

        return new GetRestaurantByIdResult(restaurant);
    }
}
