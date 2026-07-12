using Catalog.API.Messaging.EventHandlers;
using MassTransit;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// Verifies the <see cref="OrderCompletedIntegrationEventHandler"/> is
/// idempotent on <c>(OrderId, MenuItemId)</c> against a real Postgres
/// instance: two deliveries of the same event bump
/// <c>MenuItemAnalytics</c> exactly once. Exercises the
/// <c>processed_order_items</c> insert-then-fail-fast gate that the
/// in-memory provider could not (no unique-violation semantics).
/// </summary>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class OrderCompletedConsumerTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public async Task DuplicateDelivery_BumpsAnalyticsOnce()
    {
        // Arrange — seed a restaurant + menu item (MenuItemAnalytics has FKs to both).
        var restaurantId = await factory.SeedRestaurantAsync();
        var menuItemId = await factory.SeedMenuItemAsync(restaurantId);

        var completedAt = Instant.FromUtc(2026, 7, 12, 18, 30);
        var analysisDate = LocalDate.FromDateTime(completedAt.ToDateTimeUtc());
        var message = new OrderCompletedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CompletedAt = completedAt,
            Items = new[] { new OrderCompletedItem(menuItemId, 100m, 2) },
        };

        // Act — deliver the same event twice, each on its own scope (one scope per message,
        // as MassTransit does in production).
        await ConsumeAsync(message);
        await ConsumeAsync(message);

        // Assert — analytics bumped once; exactly one idempotency row.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var analytics = await db.MenuItemAnalytics
            .SingleAsync(a => a.MenuItemId == menuItemId && a.AnalysisDate == analysisDate);
        analytics.TimesOrdered.Should().Be(2, "the second delivery must be a no-op");
        analytics.TotalRevenue.Should().Be(200m);

        var processedCount = await db.ProcessedOrderItems
            .CountAsync(p => p.OrderId == message.OrderId && p.MenuItemId == menuItemId);
        processedCount.Should().Be(1);
    }

    private async Task ConsumeAsync(OrderCompletedIntegrationEvent message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var handler = new OrderCompletedIntegrationEventHandler(
            db, NullLogger<OrderCompletedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCompletedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);
    }
}
