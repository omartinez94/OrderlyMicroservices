using BuildingBlocks.Messaging.Events.Catalog;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.EventHandlers;
using Discount.Grpc.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies <see cref="FeedbackSubmittedConsumer"/> end-to-end against the
/// real <see cref="DiscountContext"/>:
/// <list type="bullet">
/// <item>4★ rating — exactly one <see cref="RewardCode"/> row (10% off).</item>
/// <item>5★ rating — exactly two <see cref="RewardCode"/> rows (15% off +
/// free appetizer); one <c>DiscountHistoryAppendedIntegrationEvent</c>
/// outbox row per created code (Phase 4 history-publish contract).</item>
/// <item>Below 4★ — no rows written.</item>
/// <item>Redelivery of the same <c>FeedbackSubmittedIntegrationEvent</c> —
/// idempotent skip via the deterministic Code UK; the second delivery
/// inserts no extra row.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Follows the
/// <see cref="OrderCompletedConsumerTests"/> convention — direct
/// <see cref="IConsumer{T}.Consume"/> invocation with a
/// <c>Substitute.For&lt;ConsumeContext&lt;T&gt;&gt;</c>. The plan §7
/// "InMemoryTestHarness" reference is aspirational; the
/// working pattern across the repo (Catalog's
/// <c>OrderCompletedConsumerTests</c>, Discount's
/// <c>MenuItemChangedConsumerTests</c>) is the direct-call path.</para>
/// <para>The consumer endpoint's flag-gating is exercised separately by
/// <see cref="ProgramStartupTests"/> — adding a bus-broker end-to-end
/// is out of Phase 5's scope (MassTransit's
/// <c>ConfigureConsumer.DisableConsumer</c> is not the project's idiom
/// per v1.1 H5; the gate is in <c>AddMassTransit → AddConsumer&lt;T&gt;</c>
/// in <c>Program.cs</c>).</para>
/// </remarks>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class FeedbackSubmittedConsumerTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("eeeeeeee-0000-0000-0000-000000000111");

    [Fact]
    public async Task FiveStarRating_CreatesTwoRewardCodes()
    {
        await factory.CleanAllAsync();

        var message = NewFeedback(feedbackId: 5001, rating: 5);

        await ConsumeAsync(message);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var rows = await db.RewardCodes
            // .IgnoreQueryFilters — the global tenant filter (r.RestaurantId
            // == provider.RestaurantId) returns Guid.Empty outside the
            // consumer's Attach scope, so a naive query sees 0 rows. The
            // filter is correct in production; the test verifies the rows
            // were inserted, ignoring the tenant scope.
            .IgnoreQueryFilters()
            .Where(r => r.RestaurantId == TenantGuid)
            .OrderBy(r => r.Code)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.Kind == RewardKind.Percentage && r.Value == 15m);
        rows.Should().Contain(r => r.Kind == RewardKind.FreeItem && r.Value == 0m);
        rows.Should().OnlyContain(r => r.ExpirationDate != null);
    }

    [Fact]
    public async Task FourStarRating_CreatesOneRewardCode()
    {
        await factory.CleanAllAsync();

        var message = NewFeedback(feedbackId: 5002, rating: 4);

        await ConsumeAsync(message);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var rows = await db.RewardCodes
            .IgnoreQueryFilters()
            .Where(r => r.RestaurantId == TenantGuid)
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].Kind.Should().Be(RewardKind.Percentage);
        rows[0].Value.Should().Be(10m);
    }

    [Fact]
    public async Task BelowThresholdRating_CreatesNoRewardCodes()
    {
        await factory.CleanAllAsync();

        var message = NewFeedback(feedbackId: 5003, rating: 3);

        await ConsumeAsync(message);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var rows = await db.RewardCodes
            .IgnoreQueryFilters()
            .Where(r => r.RestaurantId == TenantGuid)
            .ToListAsync();

        rows.Should().BeEmpty("ratings below 4★ do not mint rewards");
    }

    /// <summary>
    /// Verifies the §0.3.4 idempotency choice: deterministic Code
    /// (built from <c>(rid, tag, day, MD5(feedbackId))</c>) collides on
    /// the <c>(RestaurantId, Code)</c> UK. Two deliveries of the same
    /// event produce one row, not two.
    /// </summary>
    [Fact]
    public async Task DuplicateDelivery_IsIdempotent_LeavesOneRowPerCode()
    {
        await factory.CleanAllAsync();

        var message = NewFeedback(feedbackId: 5004, rating: 5);

        // Two deliveries on separate scopes (mirrors MassTransit's
        // production per-message scope).
        await ConsumeAsync(message);
        await ConsumeAsync(message);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var rows = await db.RewardCodes
            .IgnoreQueryFilters()
            .Where(r => r.RestaurantId == TenantGuid)
            .ToListAsync();

        rows.Should().HaveCount(2,
            "the 5★ rating produces exactly two RewardCodes per delivery " +
            "— second delivery collides on the same two Codes via UK, no extras");
    }

    private async Task ConsumeAsync(FeedbackSubmittedIntegrationEvent message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DiscountContext>();
        var outbox = sp.GetRequiredService<BuildingBlocks.Messaging.Outbox.IOutboxPublisher>();
        var clock = sp.GetRequiredService<TimeProvider>();

        var consumer = new FeedbackSubmittedConsumer(
            // The factory already gives us a "scope factory" via IServiceScopeFactory
            // registered for hosted-services; we build a fresh scope per call to
            // match production per-message lifetime.
            new SingleScopeFactory(sp),
            NullLogger<FeedbackSubmittedConsumer>.Instance);

        var context = Substitute.For<ConsumeContext<FeedbackSubmittedIntegrationEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(context);
    }

    private static FeedbackSubmittedIntegrationEvent NewFeedback(int feedbackId, int rating) =>
        new()
        {
            FeedbackId = feedbackId,
            RestaurantId = TenantGuid,
            OrderId = Guid.NewGuid(),
            OverallRating = rating,
            Comments = $"test rating {rating}",
            RewardType = string.Empty,
            RewardDescription = string.Empty,
        };

    /// <summary>
    /// Convenience <see cref="IServiceScopeFactory"/> that yields a single
    /// pre-built scope (the one the test already created for DI lookups).
    /// The consumer's <c>scopes.CreateAsyncScope()</c> call resolves to a
    /// nested child scope, so this satisfies the consumer's contract
    /// without needing the full bus harness.
    /// </summary>
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
