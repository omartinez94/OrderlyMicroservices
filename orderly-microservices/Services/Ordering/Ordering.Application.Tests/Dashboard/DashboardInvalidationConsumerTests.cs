namespace Ordering.Application.Tests.Dashboard;

using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Messaging.Events.Ordering;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Orders.EventHandlers.Integration;

public class DashboardInvalidationConsumerTests
{
    [Fact]
    public async Task Consume_OrderCreatedEvent_CallsInvalidateWithOrderSections()
    {
        Guid restaurantId = Guid.NewGuid();
        IDashboardSnapshotCache cache = Substitute.For<IDashboardSnapshotCache>();
        DashboardInvalidationConsumer consumer = new(cache, NullLogger<DashboardInvalidationConsumer>.Instance);

        OrderCreatedIntegrationEvent message = new()
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            OrderNumber = "ORD-001"
        };
        ConsumeContext<OrderCreatedIntegrationEvent> context = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(context);

        await cache.Received(1).InvalidateAsync(
            restaurantId,
            Arg.Is<IReadOnlyList<string>>(s => s.Contains("salesPace") && s.Contains("liveTicker") && s.Contains("openChecks")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_KitchenOrderBumpedEvent_CallsInvalidateWithKitchenSections()
    {
        Guid restaurantId = Guid.NewGuid();
        IDashboardSnapshotCache cache = Substitute.For<IDashboardSnapshotCache>();
        DashboardInvalidationConsumer consumer = new(cache, NullLogger<DashboardInvalidationConsumer>.Instance);

        KitchenOrderBumpedIntegrationEvent message = new()
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = NodaTime.Instant.FromUnixTimeSeconds(100)
        };
        ConsumeContext<KitchenOrderBumpedIntegrationEvent> context = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(context);

        await cache.Received(1).InvalidateAsync(
            restaurantId,
            Arg.Is<IReadOnlyList<string>>(s => s.Contains("kitchenVelocity") && s.Contains("alerts")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_EmptyRestaurantId_DoesNotCallInvalidate()
    {
        IDashboardSnapshotCache cache = Substitute.For<IDashboardSnapshotCache>();
        DashboardInvalidationConsumer consumer = new(cache, NullLogger<DashboardInvalidationConsumer>.Instance);

        OrderCreatedIntegrationEvent message = new()
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = Guid.Empty,
            OrderNumber = "ORD-000"
        };
        ConsumeContext<OrderCreatedIntegrationEvent> context = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        await cache.DidNotReceiveWithAnyArgs().InvalidateAsync(default, default, default);
    }
}
