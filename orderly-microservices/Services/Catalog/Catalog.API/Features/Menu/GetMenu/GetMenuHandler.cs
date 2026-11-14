using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Menu.GetMenu;

public sealed class GetMenuQueryValidator : AbstractValidator<GetMenuQuery>
{
    public GetMenuQueryValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().WithMessage("Restaurant identifier is required");
    }
}

internal sealed class GetMenuQueryHandler(
    IMenuReader menuReader,
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuQuery, GetMenuResult>
{
    public async Task<GetMenuResult> Handle(GetMenuQuery query, CancellationToken cancellationToken)
    {
        Guid restaurantId;
        if (Guid.TryParse(query.Identifier, out Guid parsedId))
        {
            restaurantId = parsedId;
        }
        else
        {
            Restaurant? restaurant = await dbContext.Restaurants
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ShortId == query.Identifier, cancellationToken)
                .ConfigureAwait(false);

            if (restaurant is null)
            {
                throw new NotFoundException(nameof(Restaurant), query.Identifier);
            }

            restaurantId = restaurant.Id;
        }

        currentUser.EnsureTenantAccess(restaurantId);

        MenuSnapshot? snapshot = await menuReader
            .GetByRestaurantAsync(restaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is not null)
        {
            return new GetMenuResult(snapshot);
        }

        bool restaurantExists = await dbContext.Restaurants
            .AsNoTracking()
            .AnyAsync(r => r.Id == restaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (!restaurantExists)
        {
            throw new NotFoundException(nameof(Restaurant), restaurantId);
        }

        MenuSnapshot emptySnapshot = new(
            RestaurantId: restaurantId,
            SnapshotAt: SystemClock.Instance.GetCurrentInstant(),
            Categories: []);

        return new GetMenuResult(emptySnapshot);
    }
}
