namespace Ordering.Domain.Models;

/// <summary>
/// Append-only history row recording a single state transition on an
/// <see cref="Order"/>. Appended by <c>Order.RecordActivity</c> from
/// every state-transition method on the aggregate (and from
/// <c>OrderItem.MarkItemPreparing</c> / <c>MarkItemReady</c> for
/// per-item prep transitions).
/// </summary>
/// <remarks>
/// <para>
/// Properties are <c>private set</c> — the only entry point is the
/// <see cref="Create"/> factory, which enforces null / length /
/// unknown-enum invariants. Rows are removed via cascade when the
/// parent <see cref="Order"/> is deleted; they cannot be edited in
/// place. This is the audit guarantee.
/// </para>
/// <para>
/// <see cref="CorrelationId"/> is stamped from the request-scoped
/// <c>BuildingBlocks.Correlation.CorrelationContext.Current</c> by the
/// caller; v1 accepts the ambient because the call sites are bounded
/// and the existing <c>LoggingBehavior</c> establishes the discipline.
/// </para>
/// </remarks>
public class OrderActivity : Abstractions.Entity<OrderActivityId>
{
    public OrderId OrderId { get; private set; } = default!;
    public OrderActivityType ActivityType { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Instant OccurredAt { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? Notes { get; private set; }
    public OrderActivityMetadata? Metadata { get; private set; }

    // EF Core parameterless constructor.
    private OrderActivity() { }

    /// <summary>
    /// Builds a new activity row. The factory enforces the invariants
    /// (unknown enum / oversize free-text) so the aggregate never
    /// persists a malformed row.
    /// </summary>
    /// <param name="orderId">The owning <see cref="Order"/> id.</param>
    /// <param name="activityType">Closed enum value.</param>
    /// <param name="actorUserId">Optional Guid reference (no PII).</param>
    /// <param name="occurredAt">UTC instant; stamped by the caller.</param>
    /// <param name="correlationId">Optional ambient correlation id
    /// (≤ 100 chars; null when no request/bus scope).</param>
    /// <param name="notes">Optional free-text reason (≤ 2000 chars;
    /// today only the cancellation reason uses this).</param>
    /// <param name="metadata">Optional typed status-transition
    /// snapshot; populated per the §6.1 transition callout table.</param>
    /// <exception cref="OrderActivityInvariantException">
    /// Thrown on null <paramref name="orderId"/>, unknown
    /// <paramref name="activityType"/>, or over-length free-text fields.
    /// </exception>
    public static OrderActivity Create(
        OrderId orderId,
        OrderActivityType activityType,
        Guid? actorUserId,
        Instant occurredAt,
        string? correlationId = null,
        string? notes = null,
        OrderActivityMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (!Enum.IsDefined(typeof(OrderActivityType), activityType))
            throw new OrderActivityInvariantException($"Unknown activity type: {activityType}.");

        if (correlationId is { Length: > 100 })
            throw new OrderActivityInvariantException("CorrelationId must be ≤100 chars.");

        if (notes is { Length: > 2000 })
            throw new OrderActivityInvariantException("Notes must be ≤2000 chars.");

        return new OrderActivity
        {
            Id = OrderActivityId.Of(Guid.NewGuid()),
            OrderId = orderId,
            ActivityType = activityType,
            ActorUserId = actorUserId,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            Notes = notes,
            Metadata = metadata,
        };
    }
}