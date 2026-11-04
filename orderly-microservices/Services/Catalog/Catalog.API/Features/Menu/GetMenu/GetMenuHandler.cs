using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Menu.GetMenu;

public sealed class GetMenuQueryValidator : AbstractValidator<GetMenuQuery>
{
    public GetMenuQueryValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
    }
}

internal sealed class GetMenuQueryHandler(
    IMenuReader menuReader,
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuQuery, GetMenuResult>
{
    public async Task<GetMenuResult> Handle(GetMenuQuery query, CancellationToken cancellationToken)
    {
        currentUser.EnsureTenantAccess(query.RestaurantId);

        MenuSnapshot? snapshot = await menuReader
            .GetByRestaurantAsync(query.RestaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is not null)
        {
            return new GetMenuResult(snapshot);
        }

        bool restaurantExists = await dbContext.Restaurants
            .AsNoTracking()
            .AnyAsync(r => r.Id == query.RestaurantId, cancellationToken)
            .ConfigureAwait(false);

        if (!restaurantExists)
        {
            throw new NotFoundException(nameof(Restaurant), query.RestaurantId);
        }

        MenuSnapshot emptySnapshot = new(
            RestaurantId: query.RestaurantId,
            SnapshotAt: SystemClock.Instance.GetCurrentInstant(),
            Categories: []);

        return new GetMenuResult(emptySnapshot);
    }
}
