namespace Catalog.API.Domain.Events;

/// <summary>
/// Raised when an <c>IngredientAlternative</c> row is created, updated, or
/// deleted. Dispatched by
/// <c>DispatchDomainEventsInterceptor</c>; consumed by
/// <c>IngredientAvailabilityChangedDomainEventHandler</c> which recomputes
/// every <c>MenuItem</c> whose recipe references the
/// <see cref="OriginalIngredientId"/>.
/// </summary>
/// <remarks>
/// <para>The handler uses the three FKs (<see cref="RestaurantId"/>,
/// <see cref="OriginalIngredientId"/>, <see cref="AlternativeIngredientId"/>)
/// to find affected menu items and to rebuild the alternative-edge map
/// the engine consumes.</para>
/// <para>On <see cref="ChangeKind.Deleted"/>, both <c>OriginalIngredientId</c>
/// and <c>AlternativeIngredientId</c> remain meaningful (they describe the
/// row that existed) but the engine's caller should treat the row as
/// gone when rebuilding the alternatives set.</para>
/// </remarks>
public sealed record IngredientAlternativeChangedDomainEvent(
    int AlternativeRowId,
    Guid RestaurantId,
    int OriginalIngredientId,
    int AlternativeIngredientId,
    IngredientAlternativeChangedDomainEvent.ChangeKind Kind) : IDomainEvent
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