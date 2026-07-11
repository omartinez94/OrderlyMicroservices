namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by <c>Catalog.API</c> when <c>UpdateRestaurantHandler</c>
/// mutates any of the configuration-bearing columns
/// (<c>AllowAutoSubstitute</c>, <c>AutoConfirmReservations</c>,
/// <c>TaxRate</c>, <c>Currency</c>, <c>TimeZone</c>,
/// <c>EstimatedTurnoverMinutes</c>).
/// </summary>
/// <remarks>
/// Consumers decide what to invalidate based on <see cref="ChangedFields"/>:
/// Identity re-issues claims when the configuration bound to permissions
/// changes; Discount deactivates coupons whose currency no longer matches;
/// Notification refreshes receipt templates for tax/currency placeholders
/// (per <c>CATALOG_SERVICE_PLAN.md</c> §6.5).
/// </remarks>
public record RestaurantConfigurationChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Tenant whose configuration changed.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>
    /// Names of the mutated columns (e.g. <c>["TaxRate", "Currency"]</c>).
    /// Empty when no configuration column changed (publish is skipped).
    /// </summary>
    public IReadOnlyList<string> ChangedFields { get; init; } = [];
}