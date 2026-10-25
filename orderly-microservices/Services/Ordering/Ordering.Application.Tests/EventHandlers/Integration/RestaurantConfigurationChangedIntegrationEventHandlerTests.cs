using BuildingBlocks.Messaging.Events.Catalog;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ordering.Application.Orders.EventHandlers.Integration;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.EventHandlers.Integration;

public sealed class RestaurantConfigurationChangedIntegrationEventHandlerTests
{
    private static IClock FixedClock(Instant now)
    {
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(now);
        return clock;
    }
    [Fact]
    public async Task Consume_ValidTimeZone_AddsNewRestaurantSetting()
    {
        Instant now = Instant.FromUtc(2026, 10, 24, 16, 0, 0);
        Guid restaurantId = Guid.NewGuid();

        var dbContext = Substitute.For<IApplicationDbContext>();
        var querySet = MockDbSet<RestaurantSetting>.From([]);
        var writeSet = Substitute.For<DbSet<RestaurantSetting>>();
        RestaurantSetting? added = null;
        writeSet.Add(Arg.Do<RestaurantSetting>(rs => added = rs));
        dbContext.RestaurantSettings.Returns(querySet, writeSet);

        var handler = new RestaurantConfigurationChangedIntegrationEventHandler(
            dbContext,
            FixedClock(now),
            NullLogger<RestaurantConfigurationChangedIntegrationEventHandler>.Instance);

        var consumeContext = Substitute.For<ConsumeContext<RestaurantConfigurationChangedIntegrationEvent>>();
        consumeContext.Message.Returns(new RestaurantConfigurationChangedIntegrationEvent
        {
            RestaurantId = restaurantId,
            ChangedFields = ["TimeZone"],
            TimeZone = "America/Monterrey",
        });

        await handler.Consume(consumeContext);

        added.Should().NotBeNull();
        added!.RestaurantId.Should().Be(restaurantId);
        added.TimeZoneId.Should().Be("America/Monterrey");
        added.UpdatedAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_ValidTimeZone_UpdatesExistingRestaurantSetting()
    {
        Instant now = Instant.FromUtc(2026, 10, 24, 16, 0, 0);
        Guid restaurantId = Guid.NewGuid();
        var existing = new RestaurantSetting
        {
            RestaurantId = restaurantId,
            TimeZoneId = "America/Chicago",
            UpdatedAt = now - Duration.FromHours(1),
        };

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.RestaurantSettings.Returns(MockDbSet<RestaurantSetting>.From([existing]));

        var handler = new RestaurantConfigurationChangedIntegrationEventHandler(
            dbContext,
            FixedClock(now),
            NullLogger<RestaurantConfigurationChangedIntegrationEventHandler>.Instance);

        var consumeContext = Substitute.For<ConsumeContext<RestaurantConfigurationChangedIntegrationEvent>>();
        consumeContext.Message.Returns(new RestaurantConfigurationChangedIntegrationEvent
        {
            RestaurantId = restaurantId,
            ChangedFields = ["TimeZone"],
            TimeZone = "America/Monterrey",
        });

        await handler.Consume(consumeContext);

        existing.TimeZoneId.Should().Be("America/Monterrey");
        existing.UpdatedAt.Should().Be(now);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_InvalidTimeZone_DoesNotPersist()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        var handler = new RestaurantConfigurationChangedIntegrationEventHandler(
            dbContext,
            FixedClock(Instant.FromUtc(2026, 10, 24, 16, 0, 0)),
            NullLogger<RestaurantConfigurationChangedIntegrationEventHandler>.Instance);

        var consumeContext = Substitute.For<ConsumeContext<RestaurantConfigurationChangedIntegrationEvent>>();
        consumeContext.Message.Returns(new RestaurantConfigurationChangedIntegrationEvent
        {
            RestaurantId = Guid.NewGuid(),
            ChangedFields = ["TimeZone"],
            TimeZone = "Not/A_Real_Zone",
        });

        await handler.Consume(consumeContext);

        await dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
