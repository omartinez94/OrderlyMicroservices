namespace Catalog.API.Features.PriceHistories.CreatePriceHistory;

/// <summary>
/// Internal helper that writes <see cref="PriceHistory"/> audit rows. Called
/// by every handler that mutates a price-bearing field — menu-item base price,
/// variation price modifier, alternative price modifier. The recorder is
/// scoped so it shares the request's <c>CatalogDbContext</c> (the audit row
/// commits in the same transaction as the mutation).
/// </summary>
/// <remarks>
/// Kept as an interface + implementation so tests can NSubstitute it; the
/// behavior itself is straightforward enough that an integration test on
/// each mutator is more valuable than a unit test on the recorder.
/// </remarks>
public interface IPriceHistoryRecorder
{
    /// <summary>
    /// Append a <see cref="PriceHistory"/> row to the current DbContext
    /// change tracker. The row commits with the next
    /// <c>SaveChangesAsync</c> in the calling handler.
    /// </summary>
    /// <param name="restaurantId">Tenant scope for the audit row.</param>
    /// <param name="priceType">Which price dimension changed.</param>
    /// <param name="oldPrice">Price before the mutation.</param>
    /// <param name="newPrice">Price after the mutation.</param>
    /// <param name="reason">Free-text justification (operator-supplied or generated).</param>
    /// <param name="changedByUserId">Authenticated user id; null for system-driven mutations.</param>
    /// <param name="menuItemId">Menu item id when <paramref name="priceType"/> is <see cref="PriceType.BasePrice"/>.</param>
    /// <param name="variationId">Variation id when <paramref name="priceType"/> is <see cref="PriceType.Variation"/>.</param>
    /// <param name="ingredientAlternativeId">Alternative id when <paramref name="priceType"/> is <see cref="PriceType.IngredientAlternative"/>.</param>
    /// <param name="effectiveDate">When the new price takes effect (defaults to "now").</param>
    /// <param name="ct">Cancellation token.</param>
    void Record(
        Guid restaurantId,
        PriceType priceType,
        decimal oldPrice,
        decimal newPrice,
        string reason,
        Guid? changedByUserId,
        Guid? menuItemId = null,
        int? variationId = null,
        int? ingredientAlternativeId = null,
        Instant? effectiveDate = null,
        CancellationToken ct = default);
}

internal sealed class PriceHistoryRecorder(
    CatalogDbContext dbContext,
    TimeProvider clock) : IPriceHistoryRecorder
{
    public void Record(
        Guid restaurantId,
        PriceType priceType,
        decimal oldPrice,
        decimal newPrice,
        string reason,
        Guid? changedByUserId,
        Guid? menuItemId = null,
        int? variationId = null,
        int? ingredientAlternativeId = null,
        Instant? effectiveDate = null,
        CancellationToken ct = default)
    {
        // Skip no-op writes (e.g., when an update handler is called with the
        // same value the row already has). Keeps the audit log signal-rich.
        if (oldPrice == newPrice)
        {
            return;
        }

        var row = new PriceHistory
        {
            RestaurantId = restaurantId,
            PriceType = priceType,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            Reason = reason,
            ChangedByUserId = changedByUserId ?? Guid.Empty,
            MenuItemId = menuItemId,
            VariationId = variationId,
            IngredientAlternativeId = ingredientAlternativeId,
            EffectiveDate = effectiveDate ?? SystemClock.Instance.GetCurrentInstant(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
        };

        dbContext.PriceHistories.Add(row);
    }
}