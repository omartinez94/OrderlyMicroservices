using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;

namespace Discount.Grpc.Messaging.Outbox;

/// <summary>
/// Concrete <see cref="OutboxPublisher{TContext}"/> for Discount. The publisher
/// is registered as <c>Scoped</c> in DI so it shares the same
/// <see cref="DiscountContext"/> instance — and therefore the same EF Core
/// change tracker and database transaction — as the gRPC handler that called
/// <c>PublishAsync</c>. Rows added by <see cref="OutboxPublisher{TContext}.PublishAsync{T}"/>
/// commit atomically with the aggregate mutation when the handler
/// <c>SaveChangesAsync</c>s.
/// </summary>
public sealed class DiscountOutboxPublisher(DiscountContext dbContext) : OutboxPublisher<DiscountContext>
{
    protected override DiscountContext ResolveContext() => dbContext;
}
