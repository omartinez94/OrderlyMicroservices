using BuildingBlocks.Messaging.Outbox;

namespace Ordering.Infrastructure.Data.Interceptors;

/// <summary>
/// Ordering-side implementation of <see cref="OutboxPublisher{TContext}"/>.
/// Resolves <see cref="ApplicationDBContext"/> from the ambient scope so
/// staged rows join the same transaction as the originating aggregate
/// mutation.
/// </summary>
public class OrderingOutboxPublisher(ApplicationDBContext context)
    : OutboxPublisher<ApplicationDBContext>
{
    protected override ApplicationDBContext ResolveContext() => context;
}