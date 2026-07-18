using Marten.Schema;

namespace Basket.API.Messaging;

/// <summary>
/// Marten document that stages a <c>BasketCheckoutEvent</c> for the
/// outbox dispatcher. Written by <c>CheckoutBasketCommandHandler</c> in
/// the same <see cref="IDocumentSession"/> as the Basket delete — the
/// row + the delete commit in one Postgres transaction, so a publish
/// failure can no longer lose the event and a delete failure can no
/// longer publish without releasing the cart.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="BuildingBlocks.Messaging.Outbox.OutboxMessage"/>'s
/// row shape (<c>Id / OccurredOn / Type / Payload / DispatchedAt /
/// SchemaVersion</c>) so the schema-versioning and dead-letter
/// conventions in <c>BuildingBlocks.Messaging.Outbox</c> stay
/// compatible. The Phase 2 v1 dispatcher is a Marten-flavored
/// <see cref="BackgroundService"/> (not the EF-Core
/// <c>OutboxDispatcher&lt;TContext&gt;</c>) because Basket's storage
/// is a Marten document store; see BASKET_SERVICE_PLAN.md §6 Phase 2
/// drift item 1.
/// </para>
/// <para>
/// <see cref="OccurredOn"/> and <see cref="DispatchedAt"/> are duplicated
/// into typed Postgres columns via <see cref="DuplicateFieldAttribute"/>.
/// Marten's LINQ claim query (<c>Where DispatchedAt == null OrderBy
/// OccurredOn Take(batchSize)</c>) runs against those columns instead
/// of the JSONB payload — indexable, no GIN scan needed.
/// </para>
/// <para>
/// Phase 2 v1 claim is Marten LINQ + optimistic concurrency
/// (<c>mt_version</c> column). Multi-replica safety requires a raw-SQL
/// claim with <c>FOR UPDATE SKIP LOCKED</c> — drift item 3.
/// </para>
/// </remarks>
public class CheckoutBasketOutboxMessage
{
    /// <summary>Outbox row id (also the consumer-side dedup key).</summary>
    public Guid Id { get; set; }

    /// <summary>Wall-clock moment the row was staged (Marten duplicate field).</summary>
    [DuplicateField]
    public Instant OccurredOn { get; set; }

    /// <summary>
    /// Assembly-qualified type name of the original message; the
    /// dispatcher uses this to deserialize <see cref="Payload"/> back
    /// into a concrete <c>IntegrationEvent</c> before publishing onto
    /// MassTransit.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>JSON-serialized message body.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>
    /// Wall-clock moment the row was relayed to the broker. <c>null</c>
    /// until the dispatcher publishes. The dispatcher's claim query
    /// filters on <c>DispatchedAt == null</c> (Marten duplicate field).
    /// </summary>
    [DuplicateField]
    public Instant? DispatchedAt { get; set; }

    /// <summary>
    /// Schema version of the message contract. Bumped by publishers
    /// when the payload shape changes; future consumers can drop
    /// mismatched versions into a poison queue.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
