using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Restaurants.GetRestaurantById;

public record GetRestaurantByIdQuery(string Identifier) : IQuery<GetRestaurantByIdResult>
{
    public GetRestaurantByIdQuery(Guid id) : this(id.ToString()) { }

    public Guid Id => Guid.TryParse(Identifier, out Guid guid) ? guid : Guid.Empty;
}

public record GetRestaurantByIdResult(Restaurant Restaurant);

internal class GetRestaurantByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetRestaurantByIdQuery, GetRestaurantByIdResult>
{
    public async Task<GetRestaurantByIdResult> Handle(GetRestaurantByIdQuery query, CancellationToken cancellationToken)
    {
        Restaurant? restaurant;

        if (Guid.TryParse(query.Identifier, out Guid guidId))
        {
            restaurant = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Restaurants
                    .AsNoTracking(),
                r => r.Id == guidId,
                cancellationToken);
        }
        else
        {
            restaurant = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.Restaurants
                    .AsNoTracking(),
                r => r.ShortId == query.Identifier,
                cancellationToken);
        }

        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(query.Identifier);
        }

        return new GetRestaurantByIdResult(restaurant);
    }
}
