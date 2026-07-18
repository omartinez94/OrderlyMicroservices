namespace Basket.API.Data;

/// <summary>
/// Marten-backed <see cref="IBasketRepository"/>. Every operation
/// applies the active tenant's <see cref="ICurrentRestaurantProvider.RestaurantId"/>
/// as a defence-in-depth filter on top of the pipeline-level identity
/// guard (per plan §6 Phase 1). The guard throws
/// <see cref="ForbiddenException"/> if the supplied
/// <c>(UserId, RestaurantId)</c> pair does not match the JWT claim.
/// </summary>
public class BasketRepository(IDocumentSession session, ICurrentRestaurantProvider currentRestaurantProvider)
    : IBasketRepository
{
    /// <summary>
    /// Asserts the supplied <paramref name="restaurantId"/> matches the
    /// active tenant. Returns the validated id so callers can echo it
    /// back without re-reading <see cref="ICurrentRestaurantProvider"/>.
    /// </summary>
    private Guid AssertTenant(Guid restaurantId)
    {
        var tenantId = currentRestaurantProvider.RestaurantId;
        if (tenantId == Guid.Empty || restaurantId != tenantId)
        {
            throw new ForbiddenException(
                $"Cannot operate on basket for restaurant {restaurantId} as tenant {tenantId}.");
        }
        return tenantId;
    }

    public async Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        AssertTenant(restaurantId);

        var basket = await session.Query<Models.Basket>()
            .Where(b => b.UserId == userId && b.RestaurantId == restaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        return basket is null ? throw new BasketNotFoundException(userId, restaurantId) : basket;
    }

    public async Task<Models.Basket> GetActiveCartOrEmptyAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        AssertTenant(restaurantId);

        var basket = await session.Query<Models.Basket>()
            .Where(b => b.UserId == userId && b.RestaurantId == restaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        return basket ?? new Models.Basket(userId, restaurantId);
    }

    public async Task<Models.Basket> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);
        AssertTenant(basket.RestaurantId);

        var existingBasket = await session.Query<Models.Basket>()
            .Where(b => b.UserId == basket.UserId && b.RestaurantId == basket.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingBasket is not null)
        {
            // Using ID assignment if Basket had one, but we assume entity replacement here.
            session.Delete(existingBasket);
        }

        session.Store(basket);
        await session.SaveChangesAsync(cancellationToken);

        return basket;
    }

    public async Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        AssertTenant(restaurantId);

        var basket = await session.Query<Models.Basket>()
            .Where(b => b.UserId == userId && b.RestaurantId == restaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (basket is not null)
        {
            session.Delete(basket);
            await session.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}