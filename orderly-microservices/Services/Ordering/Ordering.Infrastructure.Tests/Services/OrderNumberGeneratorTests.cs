using BuildingBlocks.Enums;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Ordering.Domain.ValueObjects;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Services;

namespace Ordering.Infrastructure.Tests.Services;

public sealed class OrderNumberGeneratorTests
{
    private static ApplicationDBContext CreateContext(string dbName)
    {
        DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDBContext(options);
    }

    [Fact]
    public async Task GenerateAsync_WithEmptyRestaurantId_ThrowsArgumentException()
    {
        using ApplicationDBContext context = CreateContext($"order-gen-{Guid.NewGuid():N}");
        IClock clock = Substitute.For<IClock>();
        OrderNumberGenerator generator = new(context, clock);

        Func<Task<OrderNumber>> act = () => generator.GenerateAsync(Guid.Empty, OrderType.DineIn);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*restaurantId cannot be empty.*");
    }

    [Fact]
    public async Task GenerateAsync_ConsecutiveCalls_GeneratesGaplessSequences()
    {
        using ApplicationDBContext context = CreateContext($"order-gen-{Guid.NewGuid():N}");
        IClock clock = Substitute.For<IClock>();
        Instant now = Instant.FromUtc(2026, 10, 23, 14, 30);
        clock.GetCurrentInstant().Returns(now);

        OrderNumberGenerator generator = new(context, clock);
        Guid restaurantId = Guid.NewGuid();

        OrderNumber first = await generator.GenerateAsync(restaurantId, OrderType.DineIn);
        OrderNumber second = await generator.GenerateAsync(restaurantId, OrderType.Takeout);
        OrderNumber third = await generator.GenerateAsync(restaurantId, OrderType.Delivery);

        first.Value.Should().Be("DIN20261023-0001");
        first.DisplayTicket.Should().Be("DIN #1");
        first.SequenceNumber.Should().Be(1);

        second.Value.Should().Be("TAK20261023-0002");
        second.DisplayTicket.Should().Be("TAK #2");
        second.SequenceNumber.Should().Be(2);

        third.Value.Should().Be("DEL20261023-0003");
        third.DisplayTicket.Should().Be("DEL #3");
        third.SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task GenerateAsync_DifferentRestaurants_MaintainsIndependentSequences()
    {
        using ApplicationDBContext context = CreateContext($"order-gen-{Guid.NewGuid():N}");
        IClock clock = Substitute.For<IClock>();
        Instant now = Instant.FromUtc(2026, 10, 23, 10, 0);
        clock.GetCurrentInstant().Returns(now);

        OrderNumberGenerator generator = new(context, clock);
        Guid restaurantA = Guid.NewGuid();
        Guid restaurantB = Guid.NewGuid();

        OrderNumber orderA1 = await generator.GenerateAsync(restaurantA, OrderType.DineIn);
        OrderNumber orderB1 = await generator.GenerateAsync(restaurantB, OrderType.DineIn);
        OrderNumber orderA2 = await generator.GenerateAsync(restaurantA, OrderType.DineIn);

        orderA1.Value.Should().Be("DIN20261023-0001");
        orderB1.Value.Should().Be("DIN20261023-0001");
        orderA2.Value.Should().Be("DIN20261023-0002");
    }

    [Fact]
    public async Task GenerateAsync_DifferentDates_ResetsSequence()
    {
        using ApplicationDBContext context = CreateContext($"order-gen-{Guid.NewGuid():N}");
        IClock clock = Substitute.For<IClock>();
        Instant day1 = Instant.FromUtc(2026, 10, 23, 12, 0);
        Instant day2 = Instant.FromUtc(2026, 10, 24, 12, 0);

        clock.GetCurrentInstant().Returns(day1);
        OrderNumberGenerator generator = new(context, clock);
        Guid restaurantId = Guid.NewGuid();

        OrderNumber day1Order1 = await generator.GenerateAsync(restaurantId, OrderType.Delivery);
        OrderNumber day1Order2 = await generator.GenerateAsync(restaurantId, OrderType.Delivery);

        clock.GetCurrentInstant().Returns(day2);
        OrderNumber day2Order1 = await generator.GenerateAsync(restaurantId, OrderType.Delivery);

        day1Order1.Value.Should().Be("DEL20261023-0001");
        day1Order2.Value.Should().Be("DEL20261023-0002");
        day2Order1.Value.Should().Be("DEL20261024-0001");
    }
}
