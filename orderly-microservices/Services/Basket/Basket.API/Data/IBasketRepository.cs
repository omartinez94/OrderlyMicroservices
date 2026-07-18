namespace Basket.API.Data;

/// <summary>
/// Persistence boundary for <see cref="Models.Basket"/>. The concrete
/// <see cref="BasketRepository"/> enforces tenant isolation via
/// <see cref="ICurrentRestaurantProvider"/>; <see cref="CachedBasketRepository"/>
/// layers a Redis cache on top without re-implementing the filter.
/// </summary>
public interface IBasketRepository
{
    /// <summary>
    /// Throws <see cref="BasketNotFoundException"/> when the active cart does
    /// not exist. Use this only for explicit lookups (admin / audit).
    /// </summary>
    Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active cart, or an empty <see cref="Models.Basket"/>
    /// projected from the supplied ids when none exists. Never throws on
    /// the missing-cart path — the <c>GET /api/v1/cart</c> contract is
    /// <c>200 + empty body</c>, never 404 (per plan §0.4.7).
    /// </summary>
    Task<Models.Basket> GetActiveCartOrEmptyAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default);

    Task<Models.Basket> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default);

    Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default);
}