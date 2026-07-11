namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by <c>Ordering.API</c> when an order reaches a terminal
/// fulfilled state (Delivered / Completed / Cancelled — TBD by the
/// Ordering-side plan that introduces this event). Consumed by
/// <c>Catalog.API</c> to drive <c>MenuItemAnalytics</c> aggregation.
/// </summary>
/// <remarks>
/// <para>Defined in Phase 2 of the Catalog service plan
/// (<c>CATALOG_SERVICE_PLAN.md</c> §7 Phase 2, decision #10) because
/// Catalog is the first consumer. The Ordering-side publish is wired
/// by the Ordering service plan, not by the Catalog plan.</para>
/// </remarks>
public record OrderCompletedIntegrationEvent : IntegrationEvent
{
    /// <summary>The order that reached the terminal state.</summary>
    public Guid OrderId { get; init; }

    /// <summary>Tenant scope for analytics aggregation.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>When the terminal transition occurred.</summary>
    public Instant CompletedAt { get; init; }

    /// <summary>Per-item line items; the consumer aggregates by <c>MenuItemId</c>.</summary>
    public IReadOnlyList<OrderCompletedItem> Items { get; init; } = [];
}

/// <summary>Per-item line item on <see cref="OrderCompletedIntegrationEvent"/>.</summary>
/// <param name="MenuItemId">The menu item sold.</param>
/// <param name="UnitPrice">Unit price at the time of fulfilment.</param>
/// <param name="Quantity">How many units were sold.</param>
public record OrderCompletedItem(Guid MenuItemId, decimal UnitPrice, int Quantity);