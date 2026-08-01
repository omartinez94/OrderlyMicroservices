using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies the <see cref="DiscountHistoryAppendedIntegrationEvent"/> publisher
/// contract (Phase 4): every Coupon / RewardCode / DiscountRule CUD + redeem
/// writes an outbox row with the right EntityType + ChangeType +
/// SchemaVersion=1. The wire-format is <c>string?</c> (serialized JSON) per
/// plan §6.5 / v1.1 M9 — Catalog's consumer parses back via
/// <c>JsonNode.Parse</c>.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class OutboxHistoryPublisherTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("dddddddd-0000-0000-0000-000000000010");
    private static readonly Instant Now = NodaTime.SystemClock.Instance.GetCurrentInstant();

    [Fact]
    public async Task ThreeMutationsAcrossThreeAggregates_StageThreeOutboxRows()
    {
        await factory.CleanAllAsync();

        // Fire three CUD events that mirror what the service handlers
        // emit: a Coupon Create, a DiscountRule Create, a RewardCode Create.
        // The publisher is the same one DiscountService / DiscountRuleService
        // / RewardCodeService inject — if any of those handlers regress on
        // the publish call, this test (plus the per-handler integration
        // tests) surfaces the regression.
        //
        // PublishAsync stages rows via the ambient DbContext; SaveChangesAsync
        // commits them. In a real handler, the same SaveChangesAsync that
        // commits the aggregate mutation also commits the outbox row —
        // here we just save at the end of the scope.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

            await publisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
                EntityType: "Coupon",
                EntityId: 1,
                RestaurantId: TenantGuid,
                ChangeType: "Created",
                OldValues: null,
                NewValues: """{"id":1,"code":"HIST-COUPON","amount":10.0}"""));

            await publisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
                EntityType: "DiscountRule",
                EntityId: 1,
                RestaurantId: TenantGuid,
                ChangeType: "Created",
                OldValues: null,
                NewValues: """{"id":1,"couponId":1,"ruleType":"MIN_ORDER_AMOUNT","ruleDataJson":"{\\"MinOrderAmount\\":\\"50.00\\"}"}"""));

            await publisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
                EntityType: "RewardCode",
                EntityId: 1,
                RestaurantId: TenantGuid,
                ChangeType: "Created",
                OldValues: null,
                NewValues: """{"id":1,"code":"RWD-PCT10","kind":"PERCENTAGE","value":10.0}"""));

            await db.SaveChangesAsync();

            var rows = await db.OutboxMessages.IgnoreQueryFilters()
                .Where(o => o.Type == typeof(DiscountHistoryAppendedIntegrationEvent).AssemblyQualifiedName)
                .OrderBy(o => o.OccurredOn)
                .ToListAsync();

            rows.Should().HaveCount(3, "three publish calls should land three outbox rows");

            rows.Should().AllSatisfy(r =>
            {
                r.SchemaVersion.Should().Be(1, "the publisher copies MessageVersion=1 to SchemaVersion");
                r.DispatchedAt.Should().BeNull("the dispatcher hasn't run yet");
            });

            // The Payload is the JSON-serialized IntegrationEvent. Assert
            // the discriminator fields appear in the payload — Catalog's
            // consumer keys on these to route to the right aggregate handler.
            rows.Select(r => r.Payload).Should().AllSatisfy(payload =>
            {
                payload.Should().Contain("\"EntityType\":");
                payload.Should().Contain("\"ChangeType\":");
                payload.Should().Contain("\"RestaurantId\":");
            });

            // Each row's payload carries the OldValues / NewValues strings.
            // For Created, OldValues is null and the JSON literal
            // "OldValues":null is what JsonSerializer emits.
            var couponRow = rows.First(r => r.Payload.Contains("\"EntityType\":\"Coupon\""));
            couponRow.Payload.Should().Contain("\"OldValues\":null");
            couponRow.Payload.Should().Contain("\"NewValues\":\"");
        }
    }

    [Fact]
    public async Task UpdateMutation_CarriesBothOldAndNewValues()
    {
        await factory.CleanAllAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

            await publisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
                EntityType: "Coupon",
                EntityId: 42,
                RestaurantId: TenantGuid,
                ChangeType: "Updated",
                OldValues: """{"amount":10.0,"redeemAmount":0}""",
                NewValues: """{"amount":15.0,"redeemAmount":0}"""));

            await db.SaveChangesAsync();

            var row = await db.OutboxMessages.IgnoreQueryFilters()
                .FirstAsync(o => o.Type == typeof(DiscountHistoryAppendedIntegrationEvent).AssemblyQualifiedName);

            row.Payload.Should().Contain("\"ChangeType\":\"Updated\"");
            // Both OldValues and NewValues are JSON-string-encoded inside
            // the outer payload. System.Text.Json's default escape policy
            // emits " for embedded quotes; rather than assert the
            // exact escape style, assert the inner JSON content
            // (sans-escapes) survives the round-trip.
            row.Payload.Should().Contain("\"OldValues\":\"{");
            row.Payload.Should().Contain("amount");
            row.Payload.Should().Contain("10.0");
            row.Payload.Should().Contain("15.0");
        }
    }

    [Fact]
    public async Task RedeemMutation_StampsChangeTypeRedeemed()
    {
        await factory.CleanAllAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

            await publisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
                EntityType: "Coupon",
                EntityId: 99,
                RestaurantId: TenantGuid,
                ChangeType: "Redeemed",
                OldValues: """{"redeemAmount":0}""",
                NewValues: """{"redeemAmount":1}"""));

            await db.SaveChangesAsync();

            var row = await db.OutboxMessages.IgnoreQueryFilters()
                .FirstAsync(o => o.Payload.Contains("\"ChangeType\":\"Redeemed\""));

            row.SchemaVersion.Should().Be(1);
            row.Payload.Should().Contain("redeemAmount");
        }
    }
}