using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Configuration knobs for the outbox dispatcher. Tests set
/// <see cref="Enabled"/> = false so unit tests skip the relay loop; the
/// integration tests leave it enabled and assert round-trip behaviour.
/// </summary>
/// <remarks>
/// Data-annotation validators run via <c>ValidateDataAnnotations().ValidateOnStart()</c>
/// in <see cref="Microsoft.Extensions.DependencyInjection.OptionsBuilderServiceCollectionExtensions"/>
/// extension points — see Discount.Grpc for the <see cref="Discount.Grpc.Options.DiscountOptions"/>
/// pattern that flips this on for production.
/// </remarks>
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

    /// <summary>
    /// Number of consecutive top-level <c>DispatchOnceAsync</c> failures that
    /// trip the dispatcher's circuit breaker. After this many failures the
    /// dispatcher pauses for <see cref="BrokerBackoffSeconds"/> and surfaces
    /// the unhealthy state through the <c>/ready</c> health check (the
    /// <c>broker-circuit</c> probe). Defined on Discount.Grpc's plan §6.7
    /// v1.2 changelog M-L10 — Discount defines the convention; other
    /// services on the same bus backend adopt the same defaults.
    /// </summary>
    /// <remarks>
    /// Only top-level <c>DispatchOnceAsync</c> throws count (TX-commit failures,
    /// broker unreachable, channel-closed). Per-row publish failures caught
    /// inside <c>DispatchBatchAsync</c> are poison rows and do not increment
    /// the counter — keeping the breaker sensitive only to broker-level outages.
    /// </remarks>
    [Range(1, 100)]
    public int MaxConsecutiveBrokerFailures { get; set; } = 3;

    /// <summary>
    /// How long the dispatcher pauses between attempts once the circuit
    /// breaker is tripped. Reset to active polling once a dispatch succeeds.
    /// Default 60s is calibrated so a long broker outage surfaces in the
    /// <c>/ready</c> probe fast enough that the LB pulls the replica from
    /// rotation, but short enough that a flapping broker doesn't permanently
    /// disable the service.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan BrokerBackoffSeconds { get; set; } = TimeSpan.FromSeconds(60);
}