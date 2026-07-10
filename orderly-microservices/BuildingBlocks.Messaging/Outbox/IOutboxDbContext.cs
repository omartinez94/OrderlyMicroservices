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
