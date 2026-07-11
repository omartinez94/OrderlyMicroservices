using NodaTime;

namespace Catalog.API.Models;

/// <summary>
/// Idempotency record for <c>OrderCompletedIntegrationEvent</c> processing.
/// Each successful upsert into <see cref="MenuItemAnalytics"/> stamps a row
/// here with the composite key <c>(OrderId, MenuItemId)</c>; the consumer's
/// unique-violation catch treats a duplicate insert as "already processed,
/// skip" (per <c>CATALOG_SERVICE_PLAN.md</c> §6.5 idempotency contract).
/// </summary>
public class ProcessedOrderItem
{
    /// <summary>The order whose terminal state was processed.</summary>
    public Guid OrderId { get; set; }

    /// <summary>The menu item whose analytics were updated.</summary>
    public Guid MenuItemId { get; set; }

    /// <summary>When the consumer stamped the row.</summary>
    public Instant ProcessedAt { get; set; }
}