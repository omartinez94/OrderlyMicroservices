namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by <c>Catalog.API</c> when a <c>Table.Status</c> mutation lands
/// — typically via the <c>UpdateTable</c> handler or implicitly by
/// reservation/walk-in seat transitions (Phase 4 onward).
/// </summary>
/// <remarks>
/// Ordering consumes this to gate new orders on
/// <c>Table.Status == Available</c>; Reservation expiry correlates
/// the hold window on this event (per <c>CATALOG_SERVICE_PLAN.md</c> §6.5).
/// </remarks>
public record TableStatusChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>The table whose status flipped.</summary>
    public Guid TableId { get; init; }

    /// <summary>Tenant scope for downstream filtering.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>
    /// Serialized <c>BuildingBlocks.Enums.TableStatus</c> (e.g.
    /// <c>"Available"</c>, <c>"Occupied"</c>, <c>"Reserved"</c>,
    /// <c>"OutOfService"</c>).
    /// </summary>
    public string NewStatus { get; init; } = default!;

    /// <summary>
    /// The order currently occupying the table, if any. <see langword="null"/>
    /// when the table is free.
    /// </summary>
    public Guid? CurrentOrderId { get; init; }
}