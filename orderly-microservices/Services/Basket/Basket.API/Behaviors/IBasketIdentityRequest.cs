namespace Basket.API.Behaviors;

/// <summary>
/// Marker interface for Basket commands / queries that carry
/// <c>(UserId, RestaurantId)</c> and must be cross-checked against the
/// caller's JWT before reaching the handler. Every cart command
/// implements this so the
/// <see cref="BasketIdentityGuardBehavior{TRequest,TResponse}"/> can
/// validate the call in a single place.
/// </summary>
public interface IBasketIdentityRequest
{
    /// <summary>Subject id asserted by the caller (from URL or body).</summary>
    Guid UserId { get; }

    /// <summary>Restaurant id asserted by the caller (from URL or body).</summary>
    Guid RestaurantId { get; }
}