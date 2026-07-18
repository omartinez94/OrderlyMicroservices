namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Base record for every cross-service integration event. Carries a
/// stable identity and a wall-clock moment captured once at construction
/// (used by consumers for correlation + dedup via
/// <see cref="Id"/>) plus a wire-format
/// <see cref="MessageVersion"/> integer used for the dual-shape rollover protocol.
///
/// <para>
/// <b>Versioning protocol:</b> when a contract needs to change in a
/// way that breaks the existing shape (rename a field, change a type,
/// drop a field), publish a new <see cref="IntegrationEvent"/> subtype
/// (e.g. <c>OrderCreatedIntegrationEventV2</c>) with
/// <see cref="MessageVersion"/> = 2. The new subtype keeps the same
/// <c>EntityName</c> via MassTransit's <c>MessageInitializer</c> so both
/// shapes route to the same consumer topic during the rollover window.
/// The dispatcher copies <see cref="MessageVersion"/> to the outbox
/// row's <c>SchemaVersion</c>; <see cref="Outbox.OutboxOptions.MaxSupportedVersion"/>
/// gates old code from forwarding v2 rows to the broker, and the
/// dead-letter table is where v2 rows land during the rollover.
/// </para>
///
/// <para>
/// <b>Additive changes (new optional fields):</b> don't need a
/// version bump. <c>System.Text.Json</c> tolerates unknown fields on the
/// read side, so an old consumer reads a new payload and ignores the
/// new field; a new consumer reads an old payload and the new field
/// is <c>default</c>.
/// </para>
/// </summary>
public record IntegrationEvent
{
    // Captured once at construction. The previous getter expressions returned a
    // fresh value per read, so MassTransit would serialize one Guid on publish and
    // the consumer would deserialize a different one — defeating correlation and
    // idempotency.
    public Guid Id { get; init; } = Guid.NewGuid();
    public Instant OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant();
    public string EventType => GetType().AssemblyQualifiedName!;

    /// <summary>
    /// Wire-format version of this event. Bumped only on breaking shape
    /// changes. New optional fields are not breaking — they
    /// don't require a bump.
    /// </summary>
    /// <remarks>
    /// Declared <c>virtual</c> so record subclasses can <c>override</c>
    /// the default (e.g., <see cref="BasketCheckoutEvent"/> overrides
    /// to <c>2</c> after dropping the raw card fields per plan §0.4.10).
    /// </remarks>
    public virtual int MessageVersion { get; init; } = 1;
}
