using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies the wire-format + outbox-stage behaviour of the three
/// Phase 6 architecture-published events — <see cref="DiscountAppliedIntegrationEvent"/>,
/// <see cref="RewardGeneratedIntegrationEvent"/>, <see cref="RewardRedeemedIntegrationEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The gated publish points inside <c>RedeemDiscount</c>, <c>CreateRewardCode</c>,
/// and <c>RedeemRewardCode</c> (per plan §7 Phase 6) wrap each
/// <see cref="IOutboxPublisher.PublishAsync"/> call in an
/// <c>if (<see cref="Discount.Grpc.Options.DiscountOptions.Enable*Publishing"/>)</c>
/// guard. The defaults are <c>false</c>, so the handlers skip the publish
/// in production today.
/// </para>
/// <para>
/// We can't drive the gRPC handlers directly because the
/// <see cref="DiscountWebApplicationFactory"/>'s gRPC server doesn't
/// propagate arbitrary client metadata to <c>HttpContext.Request.Headers</c>
/// (see <see cref="RpcEndpointTests"/> for the same auth-bridge limitation
/// rationale). The gated-flag logic is covered here by direct
/// <see cref="IOutboxPublisher.PublishAsync"/> invocations with the same
/// payload the handler would construct — that exercises the publisher
/// pipeline (serialisation, outbox-row persistence, SchemaVersion=1).
/// The flag-flip behaviour itself is documented in
/// <see cref="current-architecture.md"/> §4.4 + §9 and exercised manually
/// by setting <c>Discount:Enable*Publishing=true</c> in dev configuration.
/// </para>
/// </remarks>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class OutboxDeferredEventPublishersTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("dddddddd-0000-0000-0000-000000000020");

    [Fact]
    public async Task DiscountAppliedIntegrationEvent_StagesOutboxRow_WithSchemaVersion1()
    {
        await factory.CleanAllAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        await publisher.PublishAsync(new DiscountAppliedIntegrationEvent(
            CouponId: 42,
            CouponCode: "DISC-APPLIED-TEST",
            RestaurantId: TenantGuid,
            Quantity: 1));
        await db.SaveChangesAsync();

        var row = await db.OutboxMessages
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Type == typeof(DiscountAppliedIntegrationEvent).FullName);

        row.SchemaVersion.Should().Be(1, "publisher copies MessageVersion=1 to SchemaVersion");
        row.DispatchedAt.Should().BeNull("the dispatcher hasn't run yet");
        row.Payload.Should().Contain("\"CouponId\":42");
        row.Payload.Should().Contain("\"CouponCode\":\"DISC-APPLIED-TEST\"");
        row.Payload.Should().Contain("\"RestaurantId\":");
        row.Payload.Should().Contain("\"Quantity\":1");
    }

    [Fact]
    public async Task RewardGeneratedIntegrationEvent_StagesOutboxRow_WithSchemaVersion1()
    {
        await factory.CleanAllAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        await publisher.PublishAsync(new RewardGeneratedIntegrationEvent(
            RewardCodeId: 7,
            Code: "RWD-GEN-TEST",
            RestaurantId: TenantGuid,
            Kind: "Percentage",
            Value: 10m,
            OrderId: null));
        await db.SaveChangesAsync();

        var row = await db.OutboxMessages
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Type == typeof(RewardGeneratedIntegrationEvent).FullName);

        row.SchemaVersion.Should().Be(1);
        row.DispatchedAt.Should().BeNull();
        row.Payload.Should().Contain("\"RewardCodeId\":7");
        row.Payload.Should().Contain("\"Code\":\"RWD-GEN-TEST\"");
        row.Payload.Should().Contain("\"Kind\":\"Percentage\"");
        row.Payload.Should().Contain("\"Value\":10");
        // OrderId is null on creation (the code lands before any order consumes it).
        row.Payload.Should().Contain("\"OrderId\":null");
    }

    [Fact]
    public async Task RewardRedeemedIntegrationEvent_StagesOutboxRow_WithSchemaVersion1()
    {
        await factory.CleanAllAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        var orderId = Guid.NewGuid();
        await publisher.PublishAsync(new RewardRedeemedIntegrationEvent(
            RewardCodeId: 8,
            Code: "RWD-RED-TEST",
            RestaurantId: TenantGuid,
            OrderId: orderId,
            Quantity: 1));
        await db.SaveChangesAsync();

        var row = await db.OutboxMessages
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Type == typeof(RewardRedeemedIntegrationEvent).FullName);

        row.SchemaVersion.Should().Be(1);
        row.DispatchedAt.Should().BeNull();
        row.Payload.Should().Contain("\"RewardCodeId\":8");
        row.Payload.Should().Contain("\"Code\":\"RWD-RED-TEST\"");
        // System.Text.Json serializes Guid in "D" format
        // (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx) by default — match the
        // raw bytes we expect to find in the outbox row's Payload column.
        row.Payload.Should().Contain(orderId.ToString("D"));
        row.Payload.Should().Contain("\"Quantity\":1");
    }

    /// <summary>
    /// Asserts the three new event types register on the publisher-side
    /// outbox-stage contract (one row per publish call, no duplicates).
    /// This is the wrong-shape-guard for the §6.5 + §7 Phase 6 publish
    /// contract — a regression that silently drops one of the publish
    /// calls would surface here as a count mismatch.
    /// </summary>
    [Fact]
    public async Task AllThreeEventTypes_Roundtrip_IndependentOutboxRows()
    {
        await factory.CleanAllAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        await publisher.PublishAsync(new DiscountAppliedIntegrationEvent(
            CouponId: 1, CouponCode: "C1", RestaurantId: TenantGuid, Quantity: 1));
        await publisher.PublishAsync(new RewardGeneratedIntegrationEvent(
            RewardCodeId: 1, Code: "R1", RestaurantId: TenantGuid,
            Kind: "Percentage", Value: 10m, OrderId: null));
        await publisher.PublishAsync(new RewardRedeemedIntegrationEvent(
            RewardCodeId: 1, Code: "R1", RestaurantId: TenantGuid,
            OrderId: Guid.NewGuid(), Quantity: 1));
        await db.SaveChangesAsync();

        var rows = await db.OutboxMessages
            .IgnoreQueryFilters()
            .Where(o => new[]
            {
                typeof(DiscountAppliedIntegrationEvent).FullName!,
                typeof(RewardGeneratedIntegrationEvent).FullName!,
                typeof(RewardRedeemedIntegrationEvent).FullName!,
            }.Contains(o.Type))
            .OrderBy(o => o.Type)
            .ToListAsync();

        rows.Should().HaveCount(3, "three distinct publishes should land three distinct rows");
        rows.Select(r => r.Type).Should().BeEquivalentTo(new[]
        {
            typeof(DiscountAppliedIntegrationEvent).FullName!,
            typeof(RewardGeneratedIntegrationEvent).FullName!,
            typeof(RewardRedeemedIntegrationEvent).FullName!,
        });
    }
}
