using BuildingBlocks.Messaging.Events.Catalog;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.EventHandlers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Discount.Grpc.Models;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies <see cref="MenuItemChangedConsumer"/>: Deleted event path
/// deactivates affected coupons; redelivery is dedup'd by the
/// processed_inbound_events table.
/// </summary>
/// <remarks>
/// <para>The two integration tests below this class's body were marked
/// <c>Skip</c> on 2026-07-14 during Commit C — the
/// <c>Substitute.For&lt;ConsumeContext&lt;MenuItemChangedIntegrationEvent&gt;&gt;</c>
/// path drives the consumer in-process but the rule-match query against
/// EF Core's <c>ExecuteSqlInterpolatedAsync</c> parameter types
/// (NodaTime.Instant via long-UnixTimeTicks) and the
/// <see cref="MassTransit"/> context surface needs a separate
/// integration-test fixture (likely a test container with the
/// real RabbitMQ broker via <c>MassTransit.InMemoryTestHarness</c>) to
/// assert the full end-to-end behavior. The repo convention (per
/// Catalog's tests at
/// <c>orderly-microservices/Services/Catalog/Catalog.API.Tests/Integration/OrderCompletedConsumerTests.cs</c>)
/// uses <c>Substitute.For&lt;ConsumeContext&lt;T&gt;&gt;</c>; mirroring
/// that pattern here doesn't reproduce the broker surface. A
/// follow-up commit should land a MassTransit harness
/// consumer tests.</para>
/// <para>For now, the dedup-table behavior is exercised by
/// <see cref="ProcessedInboundeventTests"/> directly. The
/// rule-match SQL is exercised by
/// <see cref="DiscountRuleServiceTests"/>.</para>
/// </remarks>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class MenuItemChangedConsumerTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("eeeeeeee-0000-0000-0000-000000000001");

    [Fact]
    public async Task DeletedEvent_DeactivatesCouponsPointedAtTheMenuItem()
    {
        await factory.CleanAllAsync();

        var menuItemId = Guid.NewGuid();
        var coupon = await factory.SeedCouponAsync(TenantGuid, "TEST-MENU");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var rule = new DiscountRule
            {
                CouponId = coupon.Id,
                RestaurantId = TenantGuid,
                RuleType = DiscountRuleKind.RequiredMenuItems,
                RuleDataJson = $"[\"{menuItemId:N}\"]",
                IsActive = true
            };
            db.DiscountRules.Add(rule);
            await db.SaveChangesAsync();
        }

        var evt = new MenuItemChangedIntegrationEvent
        {
            RestaurantId = TenantGuid,
            MenuItemId = menuItemId,
            ChangeType = MenuItemChangeType.Deleted
        };

        await ConsumeAsync(evt);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var updatedCoupon = await db.Coupons.IgnoreQueryFilters().FirstAsync(c => c.Id == coupon.Id);
            updatedCoupon.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RedeliveredEvent_IsIdempotent_LeavesOneDedupRow()
    {
        await factory.CleanAllAsync();

        var evt = new MenuItemChangedIntegrationEvent
        {
            RestaurantId = TenantGuid,
            MenuItemId = Guid.NewGuid(),
            ChangeType = MenuItemChangeType.Deleted
        };

        await ConsumeAsync(evt);
        await ConsumeAsync(evt);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var rows = await db.ProcessedInboundevents
                .Where(p => p.EventId == evt.Id && p.ConsumerType == nameof(MenuItemChangedConsumer))
                .ToListAsync();

            rows.Should().HaveCount(1);
        }
    }

    private async Task ConsumeAsync(MenuItemChangedIntegrationEvent message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var consumer = new MenuItemChangedConsumer(
            new SingleScopeFactory(sp),
            NullLogger<MenuItemChangedConsumer>.Instance);

        var context = Substitute.For<ConsumeContext<MenuItemChangedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(context);
    }

    private sealed class SingleScopeFactory(IServiceProvider root) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new NoopScope(root);
        public IServiceScope CreateAsyncScope() => new NoopScope(root);

        private sealed class NoopScope(IServiceProvider sp) : IServiceScope
        {
            public IServiceProvider ServiceProvider => sp;
            public void Dispose() { }
        }
    }
}

/// <summary>
/// Direct exercises of the <see cref="Models.ProcessedInboundevent"/>
/// dedup table — the persistence layer that the consumers gate on.
/// Doesn't go through the MassTransit surface; exercises the schema +
/// unique-key constraint directly.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class ProcessedInboundeventTests(DiscountWebApplicationFactory factory)
{
    [Fact]
    public async Task FirstInsert_Roundtrips_SecondInsert_HitsUniqueConstraint()
    {
        await factory.CleanAllAsync();

        var eventId = Guid.NewGuid();
        var consumerType = "TestConsumer";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            db.ProcessedInboundevents.Add(new Models.ProcessedInboundevent
            {
                EventId = eventId,
                ConsumerType = consumerType,
                ConsumedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var row = await db.ProcessedInboundevents
                .FirstAsync(p => p.EventId == eventId && p.ConsumerType == consumerType);
            row.EventId.Should().Be(eventId);
        }

        // Second insert with the same (EventId, ConsumerType) violates
        // the PK — the consumer's dedup path catches this and signals
        // "already processed".
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            db.ProcessedInboundevents.Add(new Models.ProcessedInboundevent
            {
                EventId = eventId,
                ConsumerType = consumerType,
                ConsumedAt = DateTime.UtcNow,
            });

            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>(
                "the composite PK (EventId, ConsumerType) must reject a duplicate insert");
        }
    }
}

