namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by <c>Catalog.API</c> when a confirmed reservation is one
/// hour away. Notification service consumes this to dispatch an SMS /
/// email reminder. Stays in Catalog per §7 Phase 6.1 (Notification v1
/// is an out-of-plan prerequisite); the bus retains undelivered messages
/// until the consumer exists.
/// </summary>
public record ReservationReminderDueIntegrationEvent : IntegrationEvent
{
    /// <summary>Primary key of the <c>Reservation</c> row.</summary>
    public Guid ReservationId { get; init; }

    /// <summary>Tenant scope.</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>Reservation number (operator-friendly id).</summary>
    public string ReservationNumber { get; init; } = string.Empty;

    /// <summary>Customer name on the reservation.</summary>
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>Customer phone (E.164 or local format — Notification side normalises).</summary>
    public string CustomerPhone { get; init; } = string.Empty;

    /// <summary>Customer email (may be empty; Notification side picks the channel).</summary>
    public string CustomerEmail { get; init; } = string.Empty;

    /// <summary>Reservation date (UTC).</summary>
    public LocalDate ReservationDate { get; init; }

    /// <summary>Reservation time (UTC).</summary>
    public LocalTime ReservationTime { get; init; }

    /// <summary>Party size — informational, Notification side may use it for SMS template copy.</summary>
    public int PartySize { get; init; }
}