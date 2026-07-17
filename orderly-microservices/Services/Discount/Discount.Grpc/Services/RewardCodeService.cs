using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Domain;
using Discount.Grpc.Messaging.Events;
using Discount.Grpc.Models;
using Discount.Grpc.Options;
using Discount.Grpc.Validators;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Text;

namespace Discount.Grpc.Services;

/// <summary>
/// CRUD + redemption flow for the <see cref="RewardCode"/> aggregate. Six
/// RPCs: <c>CreateRewardCode</c>, <c>GetRewardCode</c>, <c>ListRewardCodes</c>,
/// <c>UpdateRewardCode</c>, <c>DeleteRewardCode</c>, <c>RedeemRewardCode</c>.
/// Reads run through the global tenant filter; CUD writes the audit columns
/// via <c>AuditableEntityInterceptor</c>; <c>RedeemRewardCode</c> uses an
/// atomic conditional UPDATE that closes the same TOCTOU race
/// <c>RedeemDiscount</c> closes for coupons. Every CUD + redeem writes a
/// <see cref="DiscountHistoryAppendedIntegrationEvent"/> outbox row per
/// plan §7 Phase 3 history-publishes row.
/// </summary>
/// <remarks>
/// <para>Why no <c>EvaluateRewardCodes</c>: reward codes are redeemed by id
/// (or by lookup against a feedback event id), not auto-applied via a
/// basket-subtotal evaluator. The apply-surface lives on the
/// <c>Coupon</c> side via <c>DiscountRule</c> + <c>EvaluateDiscountRules</c>
/// (Phase 2). RewardCode redemption is a direct lookup → redeem path.</para>
/// <para>Why a separate service from <c>DiscountService</c>: per plan
/// §0.4.3, "One Service per aggregate." Aggregating the two would push
/// proto-side message-type drift and obscure the §0.4.1 "per-RPC
/// request / response messages" rule.</para>
/// </remarks>
public class RewardCodeService(
    ILogger<RewardCodeService> logger,
    DiscountContext dbContext,
    IOutboxPublisher outbox,
    ICurrentRestaurantProvider tenantProvider,
    TimeProvider clock,
    IOptions<DiscountOptions> options)
    : RewardCodeProtoService.RewardCodeProtoServiceBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    [Permission(DiscountPermissions.RewardCodeCreate)]
    public override async Task<CreateRewardCodeResponse> CreateRewardCode(
        CreateRewardCodeRequest request,
        ServerCallContext context)
    {
        logger.LogInformation(
            "CreateRewardCode called for RestaurantId: {RestaurantId}, Code: {Code}",
            request.RewardCode.RestaurantId, request.RewardCode.Code);

        if (!Guid.TryParse(request.RewardCode.RestaurantId, out var rid))
        {
            throw new BusinessRuleException(
                $"RewardCode.RestaurantId '{request.RewardCode.RestaurantId}' is not a valid GUID.");
        }

        // UK guard — surface as FailedPrecondition with code-already-exists
        // before the DB constraint fires, mirroring the DiscountRule pattern.
        var existing = await dbContext.RewardCodes
            .FirstOrDefaultAsync(r =>
                r.RestaurantId == rid &&
                r.Code == request.RewardCode.Code);

        if (existing is not null)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"RewardCode '{request.RewardCode.Code}' already exists in this tenant. " +
                "code-already-exists=true"));
        }

        var row = RewardCodeValidator.ValidateAndBuild(
            restaurantId: rid,
            code: request.RewardCode.Code,
            kind: (RewardKind)request.RewardCode.Kind,
            value: (decimal)request.RewardCode.Value,
            description: request.RewardCode.Description,
            expirationDateIso: request.RewardCode.ExpirationDate,
            maxRedeemAmount: request.RewardCode.MaxRedeemAmount == 0
                ? null
                : request.RewardCode.MaxRedeemAmount,
            clock: clock);

        dbContext.RewardCodes.Add(row);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        // History publish (Phase 3 row in §7) — every CUD writes
        // DiscountHistoryAppendedIntegrationEvent with EntityType=RewardCode.
        // NewValues is the serialized proto model so Catalog can hydrate a
        // Marten EntityHistoryArchive document on the consumer side.
        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(RewardCode),
            EntityId: row.Id,
            RestaurantId: row.RestaurantId,
            ChangeType: "Created",
            OldValues: null,
            NewValues: SerializeNewValues(row)),
            context.CancellationToken);

        // Phase 6 (architecture-event-deferred): RewardGeneratedIntegrationEvent
        // is gated behind EnableRewardGeneratedPublishing. Wire flag = fail-secure
        // default; flips on when a downstream consumer lands (no consumer in this
        // plan's window — see plan §6.5 + §7 Phase 6).
        if (options.Value.EnableRewardGeneratedPublishing)
        {
            await outbox.PublishAsync(new RewardGeneratedIntegrationEvent(
                RewardCodeId: row.Id,
                Code: row.Code,
                RestaurantId: row.RestaurantId,
                Kind: row.Kind.ToString(),
                Value: row.Value,
                OrderId: null),
                context.CancellationToken);
        }

        return new CreateRewardCodeResponse
        {
            RewardCode = ToProtoModel(row),
            Success = true,
        };
    }

    [Permission(DiscountPermissions.RewardCodeRead)]
    public override async Task<GetRewardCodeResponse> GetRewardCode(
        GetRewardCodeRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new GetRewardCodeResponse(); // empty
        }

        var row = await dbContext.RewardCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Id == request.RewardCodeId &&
                r.RestaurantId == rid &&
                r.DeletedAt == null);

        return row is null
            ? new GetRewardCodeResponse()
            : new GetRewardCodeResponse { RewardCode = ToProtoModel(row) };
    }

    [Permission(DiscountPermissions.RewardCodeRead)]
    public override async Task<ListRewardCodesResponse> ListRewardCodes(
        ListRewardCodesRequest request,
        ServerCallContext context)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => request.PageSize,
        };

        // Global tenant filter scopes to alive rows for the calling tenant.
        var baseQuery = dbContext.RewardCodes.AsNoTracking()
            .Where(r => r.DeletedAt == null);
        var totalCount = await baseQuery.CountAsync();

        var rows = await baseQuery
            .OrderBy(r => r.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = new ListRewardCodesResponse { TotalCount = totalCount };
        response.RewardCodes.AddRange(rows.Select(ToProtoModel));
        return response;
    }

    [Permission(DiscountPermissions.RewardCodeEdit)]
    public override async Task<UpdateRewardCodeResponse> UpdateRewardCode(
        UpdateRewardCodeRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RewardCode.RestaurantId, out var rid))
        {
            return new UpdateRewardCodeResponse { Success = false };
        }

        var existing = await dbContext.RewardCodes
            .FirstOrDefaultAsync(r => r.Id == request.RewardCode.Id);

        if (existing is null)
        {
            return new UpdateRewardCodeResponse { Success = false };
        }

        // Capture OldValues before mutation so the history publish
        // (Phase 3 row in §7) can serialize the pre-image.
        var oldValues = SerializeNewValues(existing);

        RewardCodeValidator.ValidateAndBuild(
            restaurantId: rid,
            code: request.RewardCode.Code,
            kind: (RewardKind)request.RewardCode.Kind,
            value: (decimal)request.RewardCode.Value,
            description: request.RewardCode.Description,
            expirationDateIso: request.RewardCode.ExpirationDate,
            maxRedeemAmount: request.RewardCode.MaxRedeemAmount == 0
                ? null
                : request.RewardCode.MaxRedeemAmount,
            clock: clock,
            existing: existing);

        await dbContext.SaveChangesAsync(context.CancellationToken);

        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(RewardCode),
            EntityId: existing.Id,
            RestaurantId: existing.RestaurantId,
            ChangeType: "Updated",
            OldValues: oldValues,
            NewValues: SerializeNewValues(existing)),
            context.CancellationToken);

        return new UpdateRewardCodeResponse
        {
            RewardCode = ToProtoModel(existing),
            Success = true,
        };
    }

    [Permission(DiscountPermissions.RewardCodeDelete)]
    public override async Task<DeleteRewardCodeResponse> DeleteRewardCode(
        DeleteRewardCodeRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new DeleteRewardCodeResponse { Success = false };
        }

        var row = await dbContext.RewardCodes
            .FirstOrDefaultAsync(r => r.Id == request.RewardCodeId && r.RestaurantId == rid);

        if (row is null)
        {
            return new DeleteRewardCodeResponse { Success = false };
        }

        var oldValues = SerializeNewValues(row);

        // Soft-delete (mirrors Coupon's soft-delete + DiscountRuleService.DeleteDiscountRule).
        var now = Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime);
        row.DeletedAt = now;
        row.DeletedBy = DiscountActors.System;

        await dbContext.SaveChangesAsync(context.CancellationToken);

        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(RewardCode),
            EntityId: row.Id,
            RestaurantId: row.RestaurantId,
            ChangeType: "Deleted",
            OldValues: oldValues,
            NewValues: SerializeNewValues(row)),
            context.CancellationToken);

        return new DeleteRewardCodeResponse { Success = true };
    }

    [Permission(DiscountPermissions.RewardCodeRedeem)]
    public override async Task<RedeemRewardCodeResponse> RedeemRewardCode(
        RedeemRewardCodeRequest request,
        ServerCallContext context)
    {
        logger.LogInformation(
            "RedeemRewardCode called for RestaurantId: {RestaurantId}, RewardCodeId: {Id}",
            request.RestaurantId, request.RewardCodeId);

        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new RedeemRewardCodeResponse { Success = false };
        }

        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new BusinessRuleException(
                $"RedeemRewardCode.OrderId '{request.OrderId}' is not a valid GUID.");
        }

        // Defense-in-depth tenant check — the global query filter already
        // restricts, but a missing-claim scenario returns Guid.Empty from
        // ICurrentRestaurantProvider and we want to fail loud here.
        var tenantRid = tenantProvider.RestaurantId;
        if (tenantRid != Guid.Empty && tenantRid != rid)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "tenant-mismatch=true") { });
        }

        // Pre-fetch for kind-aware validation + post-update response shape.
        var row = await dbContext.RewardCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Id == request.RewardCodeId &&
                r.RestaurantId == rid &&
                r.DeletedAt == null);

        if (row is null)
        {
            return new RedeemRewardCodeResponse { Success = false };
        }

        RewardCodeValidator.ValidateRedeem(
            code: row.Code,
            restaurantId: rid,
            orderId: orderId,
            quantity: request.Quantity <= 0 ? 1 : request.Quantity,
            kind: row.Kind);

        // Atomic conditional UPDATE. SQLite serializes the row inside its
        // implicit transaction; concurrent redemptions serialize and the
        // loser sees rowsAffected = 0 instead of incrementing past
        // MaxRedeemAmount. WHERE-clause guards:
        //   - alive   (DeletedAt IS NULL)        — defensive; the read enforced this
        //   - active  (IsActive = 1)              — defensive; the global filter doesn't yet gate on IsActive
        //   - under cap (RedeemAmount < cap, OR cap unset)
        //
        // Audit-column note: raw ExecuteSqlInterpolatedAsync bypasses the
        // AuditableEntityInterceptor, so LastModifiedAt + LastModifiedBy
        // are set explicitly (plan v1.1 L11). Actor = DiscountActors.System
        // (mirrors RedeemDiscount).
        var now = Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime);
        var quantity = request.Quantity <= 0 ? 1 : request.Quantity;
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE RewardCodes
            SET RedeemAmount       = RedeemAmount + {quantity},
                RedeemedInOrderId  = {orderId},
                RedeemedAt         = {now},
                LastModifiedAt     = {now},
                LastModifiedBy     = {DiscountActors.System}
            WHERE Id = {row.Id}
              AND IsActive = 1
              AND DeletedAt IS NULL
              AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)
        ");

        if (rowsAffected == 0)
        {
            // Either (a) a concurrent redemption took the last available slot
            // just before us, or (b) an admin deactivated or soft-deleted the
            // code between our read and our write. Surface as Success = false;
            // the Idempotency-Key layer ensures the caller can safely retry
            // without double-redemption.
            return new RedeemRewardCodeResponse { Success = false };
        }

        // Re-fetch for the post-redemption proto model in the response.
        var updated = await dbContext.RewardCodes
            .AsNoTracking()
            .FirstAsync(r => r.Id == row.Id);

        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(RewardCode),
            EntityId: updated.Id,
            RestaurantId: updated.RestaurantId,
            ChangeType: "Redeemed",
            OldValues: SerializeNewValues(row),
            NewValues: SerializeNewValues(updated)),
            context.CancellationToken);

        // Phase 6 (architecture-event-deferred): RewardRedeemedIntegrationEvent
        // is gated behind EnableRewardRedeemedPublishing. Wire flag = fail-secure
        // default; flips on when a downstream consumer lands.
        if (options.Value.EnableRewardRedeemedPublishing)
        {
            await outbox.PublishAsync(new RewardRedeemedIntegrationEvent(
                RewardCodeId: updated.Id,
                Code: updated.Code,
                RestaurantId: updated.RestaurantId,
                OrderId: orderId,
                Quantity: quantity),
                context.CancellationToken);
        }

        return new RedeemRewardCodeResponse
        {
            RewardCode = ToProtoModel(updated),
            Success = true,
        };
    }

    // ---------- proto <-> entity conversion ----------

    private static RewardCodeModel ToProtoModel(RewardCode row)
    {
        return new RewardCodeModel
        {
            Id = row.Id,
            RestaurantId = row.RestaurantId.ToString(),
            Code = row.Code,
            Kind = (RewardType)row.Kind,
            Value = (double)row.Value,
            Description = row.Description ?? string.Empty,
            ExpirationDate = row.ExpirationDate?.ToString() ?? string.Empty,
            RedeemAmount = row.RedeemAmount,
            MaxRedeemAmount = row.MaxRedeemAmount ?? 0,
            RedeemedInOrderId = row.RedeemedInOrderId?.ToString() ?? string.Empty,
            RedeemedAt = row.RedeemedAt?.ToString() ?? string.Empty,
            IsActive = row.IsActive,
        };
    }

    /// <summary>Serializes the entity to a JSON string for the outbox
    /// <c>NewValues</c> / <c>OldValues</c> payload. Plan §6.5 + v1.1 M9
    /// lock the wire format as <c>string?</c> (serialized JSON), not
    /// <c>JsonObject</c> — Catalog's consumer parses back to
    /// <c>JsonObject</c> on insert via <c>JsonNode.Parse</c>.</summary>
    private static string SerializeNewValues(RewardCode row)
    {
        var model = ToProtoModel(row);
        return System.Text.Json.JsonSerializer.Serialize(model);
    }
}