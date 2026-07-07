namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Row written to the <c>outbox_messages</c> table by
/// <see cref="IOutboxPublisher"/> and consumed by the outbox dispatcher.
/// The schema is intentionally minimal: a CLR type discriminator, a JSON
/// payload, and the dispatch timestamp. Schema versioning is tracked via
/// the <see cref="SchemaVersion"/> column so a consumer can drop
/// mismatched messages into a poison queue (see follow-up plan §7.2).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Wall-clock moment the row was staged.</summary>
    public Instant OccurredOn { get; set; }

    /// <summary>
    /// Assembly-qualified type name of the original message; the dispatcher
    /// uses this to deserialize the payload back into a concrete
    /// <c>IntegrationEvent</c> before publishing onto MassTransit.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>JSON-serialized message body.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>Wall-clock moment the row was relayed to the broker.</summary>
    public Instant? DispatchedAt { get; set; }

    /// <summary>
    /// Schema version of the message contract. Bumped by publishers when
    /// a payload shape changes; consumers can use this to drop
    /// mismatched versions into a poison queue.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}