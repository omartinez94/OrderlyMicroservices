using BuildingBlocks.Messaging.Outbox;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Events;
using Discount.Grpc.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Text;

namespace Discount.Grpc.Services;

/// <summary>
/// Eligibility-predicate CRUD + evaluator for the
/// <see cref="DiscountRule"/> aggregate. Six RPCs:
/// <c>CreateDiscountRule</c>, <c>GetDiscountRule</c>, <c>ListDiscountRules</c>,
/// <c>UpdateDiscountRule</c>, <c>DeleteDiscountRule</c>, <c>EvaluateDiscountRules</c>.
/// Reads run through the global tenant filter; CUD writes the audit
/// columns via <c>AuditableEntityInterceptor</c>. CUD paths also publish
/// a <see cref="DiscountHistoryAppendedIntegrationEvent"/> to the outbox
/// (Phase 4) so Catalog's consumer can write a Marten
/// <c>EntityHistoryArchive</c> document.
/// </summary>
public class DiscountRuleService(
    ILogger<DiscountRuleService> logger,
    DiscountContext dbContext,
    IOutboxPublisher outbox)
    : DiscountRuleProtoService.DiscountRuleProtoServiceBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    [Permission(DiscountPermissions.DiscountRuleEdit)]
    public override async Task<CreateDiscountRuleResponse> CreateDiscountRule(
        CreateDiscountRuleRequest request,
        ServerCallContext context)
    {
        logger.LogInformation(
            "CreateDiscountRule called for RestaurantId: {RestaurantId}, CouponId: {CouponId}",
            request.Rule.RestaurantId, request.Rule.CouponId);

        var rule = ToEntity(request.Rule);

        // UK check (RestaurantId, CouponId) — surfaces as
        // FailedPrecondition with rule-already-exists metadata
        // when a duplicate slips past the DB constraint.
        var existing = await dbContext.DiscountRules
            .FirstOrDefaultAsync(r => r.RestaurantId == rule.RestaurantId && r.CouponId == rule.CouponId);

        if (existing is not null)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"DiscountRule for CouponId={rule.CouponId} already exists in this tenant. " +
                "rule-already-exists=true"));
        }

        dbContext.DiscountRules.Add(rule);
        await dbContext.SaveChangesAsync();

        // History publish (Phase 4) — every DiscountRule CUD writes a
        // DiscountHistoryAppendedIntegrationEvent with EntityType=DiscountRule.
        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(DiscountRule),
            EntityId: rule.Id,
            RestaurantId: rule.RestaurantId,
            ChangeType: "Created",
            OldValues: null,
            NewValues: SerializeNewValues(rule)));

        return new CreateDiscountRuleResponse
        {
            Rule = ToProtoModel(rule),
            Success = true,
        };
    }

    [Permission(DiscountPermissions.DiscountRuleRead)]
    public override async Task<GetDiscountRuleResponse> GetDiscountRule(
        GetDiscountRuleRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new GetDiscountRuleResponse(); // empty
        }

        var rule = await dbContext.DiscountRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Id == request.RuleId &&
                r.RestaurantId == rid &&
                r.DeletedAt == null);

        return rule is null
            ? new GetDiscountRuleResponse()
            : new GetDiscountRuleResponse { Rule = ToProtoModel(rule) };
    }

    [Permission(DiscountPermissions.DiscountRuleRead)]
    public override async Task<ListDiscountRulesResponse> ListDiscountRules(
        ListDiscountRulesRequest request,
        ServerCallContext context)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => request.PageSize,
        };

        // Global tenant filter scopes to active rules for the calling tenant.
        var baseQuery = dbContext.DiscountRules.AsNoTracking()
            .Where(r => r.DeletedAt == null);
        var totalCount = await baseQuery.CountAsync();

        var rows = await baseQuery
            .OrderBy(r => r.CouponId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = new ListDiscountRulesResponse { TotalCount = totalCount };
        response.Rules.AddRange(rows.Select(ToProtoModel));
        return response;
    }

    [Permission(DiscountPermissions.DiscountRuleEdit)]
    public override async Task<UpdateDiscountRuleResponse> UpdateDiscountRule(
        UpdateDiscountRuleRequest request,
        ServerCallContext context)
    {
        var rule = await dbContext.DiscountRules
            .FirstOrDefaultAsync(r => r.Id == request.Rule.Id);

        if (rule is null)
        {
            return new UpdateDiscountRuleResponse { Success = false };
        }

        // Capture OldValues before mutation so the history publish
        // (Phase 4) can serialize the pre-image.
        var oldValues = SerializeNewValues(rule);

        rule.RuleType = (DiscountRuleKind)request.Rule.RuleType;
        rule.RuleDataJson = request.Rule.RuleDataJson;
        rule.IsActive = request.Rule.IsActive;

        await dbContext.SaveChangesAsync();

        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(DiscountRule),
            EntityId: rule.Id,
            RestaurantId: rule.RestaurantId,
            ChangeType: "Updated",
            OldValues: oldValues,
            NewValues: SerializeNewValues(rule)));

        return new UpdateDiscountRuleResponse
        {
            Rule = ToProtoModel(rule),
            Success = true,
        };
    }

    [Permission(DiscountPermissions.DiscountRuleEdit)]
    public override async Task<DeleteDiscountRuleResponse> DeleteDiscountRule(
        DeleteDiscountRuleRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new DeleteDiscountRuleResponse { Success = false };
        }

        var rule = await dbContext.DiscountRules
            .FirstOrDefaultAsync(r => r.Id == request.RuleId && r.RestaurantId == rid);

        if (rule is null)
        {
            return new DeleteDiscountRuleResponse { Success = false };
        }

        // Soft-delete (mirrors Coupon's soft-delete).
        var oldValues = SerializeNewValues(rule);

        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        rule.DeletedAt = now;
        rule.DeletedBy = DiscountActors.Service;

        await dbContext.SaveChangesAsync();

        await outbox.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(
            EntityType: nameof(DiscountRule),
            EntityId: rule.Id,
            RestaurantId: rule.RestaurantId,
            ChangeType: "Deleted",
            OldValues: oldValues,
            NewValues: SerializeNewValues(rule)));

        return new DeleteDiscountRuleResponse { Success = true };
    }

    [Permission(DiscountPermissions.DiscountRuleRead)]
    public override async Task<EvaluateDiscountRulesResponse> EvaluateDiscountRules(
        EvaluateDiscountRulesRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var rid))
        {
            return new EvaluateDiscountRulesResponse();
        }

        var rules = await dbContext.DiscountRules
            .AsNoTracking()
            .Where(r => r.RestaurantId == rid && r.IsActive && r.DeletedAt == null)
            .ToListAsync();

        var menuItemIds = new List<string>(request.MenuItemIds)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToHashSet();

        var response = new EvaluateDiscountRulesResponse();
        foreach (var rule in rules)
        {
            if (Matches(rule, request.OrderTotal, menuItemIds))
            {
                response.ApplicableCouponIds.Add(rule.CouponId);
            }
        }

        return response;
    }

    /// <summary>Evaluates a single rule against the supplied order total
    /// and menu-item set. JSON payload parsing is deliberately permissive
    /// here — bad JSON skips the rule (defensive eval)
    /// "rules stay indexable in SQL; the JSON column keeps rule kinds
    /// flexible".</summary>
    private static bool Matches(DiscountRule rule, double orderTotal, HashSet<Guid> menuItemIds)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleDataJson) || rule.RuleDataJson == "{}")
        {
            return true; // empty rule = no filter, always matches.
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rule.RuleDataJson);
            var root = doc.RootElement;

            return rule.RuleType switch
            {
                DiscountRuleKind.MinOrderAmount =>
                    root.TryGetProperty("MinOrderAmount", out var minProp) &&
                    decimal.TryParse(minProp.GetString(), out var min) &&
                    (decimal)orderTotal >= min,

                DiscountRuleKind.RequiredMenuItems =>
                    root.TryGetProperty("RequiredMenuItemIds", out var idsProp) &&
                    idsProp.ValueKind == System.Text.Json.JsonValueKind.Array &&
                    EnumerableContainsAny(idsProp, menuItemIds),

                DiscountRuleKind.TimeWindow =>
                    // keeps TimeWindow permissive (always matches).
                    // wires the cron evaluator that interprets
                    // StartTime + EndTime + DayOfWeekMask.
                    true,

                DiscountRuleKind.Bogo =>
                    root.TryGetProperty("BuyQuantity", out var buyProp) &&
                    root.TryGetProperty("GetQuantity", out var getProp) &&
                    buyProp.GetInt32() > 0 &&
                    getProp.GetInt32() > 0,

                _ => false,
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool EnumerableContainsAny(
        System.Text.Json.JsonElement arrayElement,
        HashSet<Guid> haystack)
    {
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.String &&
                Guid.TryParse(item.GetString(), out var g) &&
                haystack.Contains(g))
            {
                return true;
            }
        }
        return false;
    }

    // ---------- proto <-> entity conversion ----------

    private static DiscountRule ToEntity(DiscountRuleModel model)
    {
        var rule = new DiscountRule
        {
            RestaurantId = Guid.Parse(model.RestaurantId),
            CouponId = model.CouponId,
            RuleType = (DiscountRuleKind)model.RuleType,
            RuleDataJson = model.RuleDataJson,
            IsActive = model.IsActive,
        };
        if (model.Id > 0)
        {
            // For updates: respect the proto-supplied Id. EF tracks new
            // rows with Id default(0), so we only set this for updates.
            typeof(DiscountRule).GetProperty(nameof(DiscountRule.Id))!
                .SetValue(rule, model.Id);
        }
        return rule;
    }

    private static DiscountRuleModel ToProtoModel(DiscountRule rule)
    {
        var model = new DiscountRuleModel
        {
            Id = rule.Id,
            RestaurantId = rule.RestaurantId.ToString(),
            CouponId = rule.CouponId,
            RuleType = (DiscountRuleType)rule.RuleType,
            RuleDataJson = rule.RuleDataJson,
            IsActive = rule.IsActive,
        };
        return model;
    }

    /// <summary>Serializes the entity to a JSON string for the outbox
    /// <c>NewValues</c> / <c>OldValues</c> payload. Plan §6.5 + v1.1 M9
    /// lock the wire format as <c>string?</c> (serialized JSON), not
    /// <c>JsonObject</c> — Catalog's consumer parses back to
    /// <c>JsonObject</c> on insert via <c>JsonNode.Parse</c>.</summary>
    private static string SerializeNewValues(DiscountRule rule)
    {
        var model = ToProtoModel(rule);
        return System.Text.Json.JsonSerializer.Serialize(model);
    }
}
