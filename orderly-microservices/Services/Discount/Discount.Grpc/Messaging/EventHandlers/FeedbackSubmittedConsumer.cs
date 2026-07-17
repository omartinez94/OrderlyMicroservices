using System.Security.Cryptography;
using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Events;
using Discount.Grpc.Models;
using Discount.Grpc.Validators;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Discount.Grpc.Messaging.EventHandlers;

/// <summary>
/// Reacts to <see cref="FeedbackSubmittedIntegrationEvent"/> (published by
/// Notification v1 per plan §6.6.3) and issues per-rating reward codes.
/// Implementation notes:
/// <list type="bullet">
/// <item>Disabled by default via <c>DiscountOptions:EnableFeedbackSubmittedConsumer=false</c>;
/// the conditional <c>AddConsumer</c> registration in <c>Program.cs</c>
/// gates the consumer endpoint.</item>
/// <item>Hardcoded 4★/5★ rule from plan §6.6.3:
/// <c>rating ∈ [4, 5)</c> → one percentage reward (10%);
/// <c>rating ≥ 5</c> → two rewards (15% + free appetizer).</item>
/// <item>Idempotency: deterministic <see cref="RewardCode.Code"/> helpers
/// (per Phase 3) ensure a redelivered <c>FeedbackSubmittedIntegrationEvent</c>
/// collides on the same <c>(RestaurantId, Code)</c> UK. A pre-check
/// + <c>DbUpdateException</c> swallow handle the dedup side per
/// plan §0.3.4 consumer-side choice matrix
/// ("RewardCode.Code unique constraint via the Code*() helpers — no
/// separate table needed").</item>
/// <item>Pattern 2 synthetic principal (plan §0.4 / Q10) attaches the
/// per-event <c>restaurantId</c> to <see cref="ICurrentRestaurantProvider"/>
/// for the scope of <see cref="Consume"/>, so the existing
/// <c>AuditableEntityInterceptor</c> + tenant-aware repos behave as if
/// the call had come in via an RPC.</item>
/// </list>
/// </summary>
/// <remarks>
/// <b>Plan-sketch deviation (v1.2):</b> plan §7 Phase 5 sketched a
/// <c>await sender.Send(new CreateRewardCodeCommand(...))</c> via
/// MediatR <c>ISender</c>. The codebase does not have a
/// <c>CreateRewardCodeCommand</c> abstraction — <c>RewardCodeService</c>
/// implements the gRPC methods directly with raw
/// <see cref="DiscountContext"/>. Phase 5 mirrors the established
/// <c>MenuItemChangedConsumer</c> pattern (scope + attach + raw
/// <c>DbContext</c> writes) rather than introducing a MediatR command
/// not asked for by the rest of Phase 4. The §0.3.4 idempotency
/// strategy is preserved (Code UK collision — no
/// <c>processed_inbound_events</c> row).
/// </remarks>
public sealed class FeedbackSubmittedConsumer(
    IServiceScopeFactory scopes,
    ILogger<FeedbackSubmittedConsumer> logger)
    : IConsumer<FeedbackSubmittedIntegrationEvent>
{
    private const string ConsumerType = nameof(FeedbackSubmittedConsumer);

    public async Task Consume(ConsumeContext<FeedbackSubmittedIntegrationEvent> context)
    {
        var evt = context.Message;
        var rid = evt.RestaurantId;

        // Stable per-feedback anchor: MD5(feedbackId bytes) → 16-byte Guid.
        // The Code*() helpers render this as a substring; same feedback id
        // always produces the same Code regardless of when the consumer
        // fires (the day-bucket in the Code string is the human-readable
        // prefix; the event-id-derived anchor is the actual idempotency
        // key per v1.2 H-L1).
        var feedbackAnchor = GuidFromInt(evt.FeedbackId);

        // Pattern 2 synthetic principal (plan §0.4 / Q10).
        var principal = new ClaimsPrincipalBuilder()
            .WithRestaurant(rid)
            .WithActor(DiscountActors.Service)
            .Build();

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var provider = sp.GetRequiredService<ICurrentRestaurantProvider>();
        var db = sp.GetRequiredService<DiscountContext>();
        var outbox = sp.GetRequiredService<IOutboxPublisher>();
        var clock = sp.GetRequiredService<TimeProvider>();
        // Mirror MenuItemChangedConsumer.cs:101 — raw SQL / TimeProvider
        // extensions don't ship `GetCurrentInstant()`; we read the
        // canonical clock from NodaTime.SystemClock instead so the
        // Code*() helper (which takes a TimeProvider) sees the same
        // wall-clock this consumer publishes against.
        var now = SystemClock.Instance.GetCurrentInstant();

        using (provider.Attach(principal))
        {
            // 4★ < r < 5  →  10% off
            if (evt.OverallRating >= 4 && evt.OverallRating < 5)
            {
                await CreateAndPublishAsync(
                    db, outbox, clock, now,
                    rid: rid,
                    code: RewardCode.Code4StarPct10(rid, feedbackAnchor, clock),
                    kind: RewardKind.Percentage,
                    value: 10m,
                    description: "10% off for 4★ feedback",
                    ct: context.CancellationToken);
                logger.LogInformation(
                    "FeedbackSubmitted {FeedbackId} for {RestaurantId} rating={Rating}: 4★ reward issued.",
                    evt.FeedbackId, rid, evt.OverallRating);
                return;
            }

            // 5★  →  15% off  +  free appetizer
            if (evt.OverallRating >= 5)
            {
                await CreateAndPublishAsync(
                    db, outbox, clock, now,
                    rid: rid,
                    code: RewardCode.Code5StarPct15(rid, feedbackAnchor, clock),
                    kind: RewardKind.Percentage,
                    value: 15m,
                    description: "15% off for 5★ feedback",
                    ct: context.CancellationToken);
                await CreateAndPublishAsync(
                    db, outbox, clock, now,
                    rid: rid,
                    code: RewardCode.Code5StarAppetizer(rid, feedbackAnchor, clock),
                    kind: RewardKind.FreeItem,
                    value: 0m,
                    description: "Free appetizer for 5★ feedback",
                    ct: context.CancellationToken);
                logger.LogInformation(
                    "FeedbackSubmitted {FeedbackId} for {RestaurantId} rating={Rating}: 5★ rewards issued.",
                    evt.FeedbackId, rid, evt.OverallRating);
                return;
            }

            // Below threshold — consume the message but no row written.
            // Notification v1 may still want the message processed for
            // its own bookkeeping; this consumer simply chooses not to
            // mint a reward.
            logger.LogInformation(
                "FeedbackSubmitted {FeedbackId} for {RestaurantId} rating={Rating}: below reward threshold; ack-only.",
                evt.FeedbackId, rid, evt.OverallRating);
        }
    }

    private async Task CreateAndPublishAsync(
        DiscountContext db,
        IOutboxPublisher outbox,
        TimeProvider clock,
        Instant now,
        Guid rid,
        string code,
        RewardKind kind,
        decimal value,
        string description,
        CancellationToken ct)
    {
        // §0.3.3 enforces kind-specific Value rules + ExpirationDate > now
        // when set. The validator builds the row in one shot, matching the
        // RewardCodeService.CreateRewardCode UK-guard pattern.
        var row = RewardCodeValidator.ValidateAndBuild(
            restaurantId: rid,
            code: code,
            kind: kind,
            value: value,
            description: description,
            expirationDateIso: null,
            maxRedeemAmount: null,
            clock: clock);
        row.ExpirationDate = now + Duration.FromDays(30);

        // Pre-check (idempotency sink; mirrors RewardCodeService.CreateRewardCode).
        var existing = await db.RewardCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.RestaurantId == rid && r.Code == code,
                ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "FeedbackSubmitted: RewardCode '{Code}' for {RestaurantId} already exists (idempotent skip).",
                code, rid);
            return;
        }

        db.RewardCodes.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race: a parallel dispatcher / redelivered message inserted the
            // same Code before our SaveChangesAsync committed. Swallow —
            // the deterministic Code ensures the system state is identical
            // regardless of which instance wins.
            logger.LogInformation(
                "FeedbackSubmitted: UK collision on RewardCode '{Code}' for {RestaurantId} (concurrent insert; idempotent skip).",
                code, rid);
            return;
        }

        // Phase 4 history-publish contract — every RewardCode CUD writes
        // one DiscountHistoryAppendedIntegrationEvent outbox row, which
        // Catalog's consumer materializes as a Marten EntityHistoryArchive
        // document per plan §6.6.1.
        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(RewardCode),
            EntityId: row.Id,
            RestaurantId: row.RestaurantId,
            ChangeType: "Created",
            OldValues: null,
            NewValues: SerializeNewValues(row)),
            ct);
    }

    /// <summary>
    /// Phase 4 wire format: <c>NewValues</c> is a JSON snapshot of the
    /// materialised row (per plan §6.5 + v1.1 M9). Keeps
    /// <c>DiscountHistoryAppendedIntegrationEvent.NewValues</c> as
    /// <c>string</c> on both sides — Catalog's Marten consumer parses
    /// back to <c>JsonObject</c> on insert.
    /// </summary>
    private static string SerializeNewValues(RewardCode row) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            row.Id,
            row.RestaurantId,
            row.Code,
            row.Kind,
            row.Value,
            row.Description,
            row.ExpirationDate,
            row.RedeemAmount,
            row.MaxRedeemAmount,
        });

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqlite
        && sqlite.SqlState is "1555" or "2067";

    /// <summary>
    /// Stable per-feedback anchor: MD5 of the int's bytes → 16 bytes →
    /// Guid. Same <paramref name="feedbackId"/> always yields the same
    /// Guid, so the deterministic <see cref="RewardCode.Code"/> builder
    /// (which embeds the Guid) yields the same Code on redelivery.
    /// </summary>
    internal static Guid GuidFromInt(int feedbackId)
    {
        var hash = MD5.HashData(BitConverter.GetBytes(feedbackId));
        return new Guid(hash);
    }
}
