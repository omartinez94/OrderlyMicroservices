namespace Catalog.API.Domain.Events;

/// <summary>
/// Raised when an <c>Ingredient</c> row is created, updated, or deleted.
/// Dispatched by the in-process
/// <c>DispatchDomainEventsInterceptor</c>; consumed by
/// <c>IngredientAvailabilityChangedDomainEventHandler</c> which recomputes
/// every <c>MenuItem</c> that references this ingredient.
/// </summary>
/// <remarks>
/// <para>The handler queries <c>MenuItemIngredients</c> for every
/// <c>MenuItemId</c> whose recipe includes this ingredient, then runs
/// the engine per affected menu item.</para>
/// <para>Carries <c>RestaurantId</c> so the handler can scope its
/// <c>ICatalogCache.InvalidateIngredientsAsync</c> call without an
/// extra DB roundtrip.</para>
/// </remarks>
public sealed record IngredientChangedDomainEvent(
    int IngredientId,
    Guid RestaurantId,
    IngredientChangedDomainEvent.ChangeKind Kind) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public Instant OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant();

    /// <summary>Discriminator for the mutation that raised this event.</summary>
    public enum ChangeKind
    {
        Created,
        Updated,
        Deleted,
    }
}