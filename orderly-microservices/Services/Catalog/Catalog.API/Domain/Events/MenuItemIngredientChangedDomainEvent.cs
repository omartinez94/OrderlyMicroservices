namespace Catalog.API.Domain.Events;

/// <summary>
/// Raised when a <c>MenuItemIngredient</c> row (the link between a menu
/// item and an ingredient) is added or removed. Dispatched by
/// <c>DispatchDomainEventsInterceptor</c>; consumed by
/// <c>IngredientAvailabilityChangedDomainEventHandler</c> which recomputes
/// the single <see cref="MenuItemId"/> named on the event.
/// </summary>
/// <remarks>
/// Only <see cref="ChangeKind.Created"/> and
/// <see cref="ChangeKind.Deleted"/> are emitted today — soft updates are
/// not in scope.
/// </remarks>
public sealed record MenuItemIngredientChangedDomainEvent(
    int LinkId,
    Guid MenuItemId,
    int IngredientId,
    MenuItemIngredientChangedDomainEvent.ChangeKind Kind) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public Instant OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant();

    /// <summary>Discriminator for the mutation that raised this event.</summary>
    public enum ChangeKind
    {
        Created,
        Deleted,
    }
}