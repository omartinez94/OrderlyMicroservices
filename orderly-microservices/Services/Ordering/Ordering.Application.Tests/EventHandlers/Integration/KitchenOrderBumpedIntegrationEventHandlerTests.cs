using MassTransit;

namespace Ordering.Application.Tests.EventHandlers.Integration;

/// <summary>
/// Covers <see cref="KitchenOrderBumpedIntegrationEventHandler"/>. The
/// "bumped" signal is recorded for audit only — no aggregate mutation today.
/// This test exists to lock in the no-op behaviour (no exception, no DB
/// call) so a future change that inadvertently starts mutating <c>Order</c>
/// fails loudly.
/// </summary>
public sealed class KitchenOrderBumpedIntegrationEventHandlerTests
{
    [Fact]
    public async Task Consume_LogsAndCompletes()
    {
        var consumer = new KitchenOrderBumpedIntegrationEventHandler(
            NullLogger<KitchenOrderBumpedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(new KitchenOrderBumpedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = SystemClock.Instance.GetCurrentInstant()
        });

        // No exception, no return value: the handler completes cleanly.
        await consumer.Consume(context);
    }
}