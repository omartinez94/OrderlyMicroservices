using BuildingBlocks.Messaging.Outbox;
using Kitchen.API.Infrastructure.Data;

namespace Kitchen.API.Infrastructure.Interceptors;

/// <summary>
/// Kitchen-side implementation of <see cref="OutboxPublisher{TContext}"/>.
/// Resolves <see cref="KitchenDbContext"/> from the ambient scope so
/// staged rows join the same transaction as the originating aggregate
/// mutation.
/// </summary>
public class KitchenOutboxPublisher(KitchenDbContext context)
    : OutboxPublisher<KitchenDbContext>
{
    protected override KitchenDbContext ResolveContext() => context;
}