namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Quarantine row for <see cref="OutboxMessage"/> payloads the dispatcher
/// couldn't route. Today the only reason a row lands here is
/// <see cref="Reason.UnsupportedSchemaVersion"/> — the row was staged with
/// <see cref="OutboxMessage.SchemaVersion"/> &gt;
/// <see cref="OutboxOptions.MaxSupportedVersion"/>, so the dispatcher
/// cannot deserialize it safely and copies it here for triage instead of
/// publishing.
///
/// Mirrors <see cref="OutboxMessage"/>'s shape so an operator can pull
/// the row out and either bump <c>MaxSupportedVersion</c> (after deploying
/// a new consumer) or patch the payload.
/// </summary>
public class OutboxDeadMessage
{
    public Guid Id { get; set; }

    /// <summary>Wall-clock moment the row was originally staged.</summary>
    public Instant OccurredOn { get; set; }

    /// <summary>Assembly-qualified type name of the original message.</summary>
    public string Type { get; set; } = default!;

    /// <summary>JSON-serialized message body.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>Schema version that was stamped by the publisher.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Why the row was dead-lettered.</summary>
    public string Reason { get; set; } = default!;

    /// <summary>Wall-clock moment the dispatcher quarantined the row.</summary>
    public Instant RejectedAt { get; set; }
}

/// <summary>
/// Closed set of "why this row died" values the dispatcher writes into
/// <see cref="OutboxDeadMessage.Reason"/>. New reasons are added here as
/// new dispatcher-side gates come online; operators should grep the
/// poison table by reason to see what fraction of rows need a consumer
/// upgrade vs. payload patch.
/// </summary>
public static class Reasons
{
    public const string UnsupportedSchemaVersion = "unsupported_schema_version";
}
