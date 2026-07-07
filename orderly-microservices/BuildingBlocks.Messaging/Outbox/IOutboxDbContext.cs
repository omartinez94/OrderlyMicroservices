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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}