using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Contract a service's application <see cref="DbContext"/> exposes to the
/// outbox machinery. Each service implements this on its primary
/// <c>DbContext</c> so <see cref="OutboxPublisher{TContext}"/> and
/// <see cref="OutboxDispatcher{TContext}"/> can use the same connection /
/// transaction as the aggregate mutation, which is the whole point of
/// the outbox.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>
    /// Quarantine table for rows the dispatcher can't route. Populated
    /// when a row's <see cref="OutboxMessage.SchemaVersion"/> exceeds
    /// <see cref="OutboxOptions.MaxSupportedVersion"/>. The dispatcher
    /// copies the row here, deletes it from the live table, and skips
    /// the broker publish — operators triage from
    /// <c>outbox_messages_dead</c> either by bumping
    /// <see cref="OutboxOptions.MaxSupportedVersion"/> (after deploying
    /// a new consumer) or by patching the payload and replaying.
    /// </summary>
    DbSet<OutboxDeadMessage> OutboxDeadMessages { get; }

    /// <summary>
    /// Access to the underlying EF Core <see cref="DatabaseFacade"/> so
    /// the dispatcher can open an explicit transaction. The explicit
    /// transaction is what holds the engine-native row locks (Postgres
    /// <c>FOR UPDATE SKIP LOCKED</c>, MSSQL <c>WITH (ROWLOCK, UPDLOCK,
    /// READPAST)</c>) alive across the broker publish + the dispatched-on
    /// stamp.
    /// </summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
