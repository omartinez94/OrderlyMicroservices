namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Configuration knobs for the outbox dispatcher. Tests set
/// <see cref="Enabled"/> = false so unit tests skip the relay loop; the
/// integration tests leave it enabled and assert round-trip behaviour.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Master switch. When false, the dispatcher IHostedService is
    /// not registered and any direct calls to <see cref="IOutboxPublisher"/>
    /// still write to the outbox table (so the dispatcher can be re-enabled
    /// later without re-plumbing callers).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Poll interval when the outbox has undispatched rows.</summary>
    public TimeSpan ActivePollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Poll interval when the outbox is empty (low-cost idle).</summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum rows the dispatcher claims per poll.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Highest <see cref="OutboxMessage.SchemaVersion"/> the dispatcher
    /// will route to <c>IPublishEndpoint</c>. Rows stamped with a higher
    /// version are copied to <c>outbox_messages_dead</c> instead of
    /// being published. Bump this together with the consumer code so the
    /// two stay in lockstep; the dispatcher is the gate because it sits
    /// at the bus boundary.
    /// </summary>
    public int MaxSupportedVersion { get; set; } = 1;
}