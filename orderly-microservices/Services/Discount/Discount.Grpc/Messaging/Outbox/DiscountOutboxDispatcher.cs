using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;

namespace Discount.Grpc.Messaging.Outbox;

/// <summary>
/// First SQLite <see cref="OutboxDispatcher{TContext}"/> implementation. Extends
/// the shared <c>OutboxDispatcher</c> base class with two service hooks:
///
/// <list type="bullet">
/// <item><see cref="CreateContext"/> — resolves a fresh <see cref="DiscountContext"/>
/// from the per-poll scope so a broker failure rolls back cleanly without
/// poisoning the surrounding request scope.</item>
/// <item><see cref="BuildClaimSql"/> — emits a SELECT of undispatched rows.
/// SQLite serializes writes via the database lock held by
/// <c>BeginTransactionAsync</c> in the base class; for single-replica
/// deployments this is sufficient.</item>
/// </list>
///
/// <para>
/// The multi-replica <c>ClaimId</c>-based claiming pattern is
/// deferred to a follow-up. SQLite has no <c>SKIP LOCKED</c> equivalent; we
/// stage the column via a follow-up migration when HA is in scope.
/// </para>
/// </summary>
public sealed class DiscountOutboxDispatcher(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<OutboxOptions> options,
    Microsoft.Extensions.Logging.ILogger<DiscountOutboxDispatcher> logger)
    : OutboxDispatcher<DiscountContext>(services, options, logger)
{
    /// <inheritdoc />
    protected override DiscountContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<DiscountContext>();

    /// <inheritdoc />
    /// <remarks>
    /// SQLite claim SQL. The base class wraps the FromSql call in
    /// <c>BeginTransactionAsync</c>; the engine-level database lock held by
    /// that transaction prevents any other process from claiming the same
    /// rows in flight.
    /// </remarks>
    protected override FormattableString BuildClaimSql(int batchSize) =>
        $"SELECT * FROM outbox_messages WHERE DispatchedAt IS NULL ORDER BY OccurredOn ASC LIMIT {batchSize}";
}
