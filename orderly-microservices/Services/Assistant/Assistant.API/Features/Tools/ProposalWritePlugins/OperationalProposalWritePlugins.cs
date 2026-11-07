using System.Text.Json;
using Assistant.API.Infrastructure.Security;

namespace Assistant.API.Features.Tools.ProposalWritePlugins;

/// <summary>
/// Base class for stateless Human-in-the-Loop (HITL) write tools.
/// Never mutates state during the LLM turn; instead emits an HMAC-SHA256 signed <see cref="ActionProposalDto"/>
/// and immediately ends the turn so user confirmation executes deterministically with 0 extra LLM tokens.
/// </summary>
public abstract class ProposalWriteToolPluginBase(IActionProposalSigner signer) : IAiToolPlugin
{
    public abstract string PluginName { get; }
    public abstract string ToolName { get; }
    public abstract string Description { get; }
    public bool IsProposalWriteTool => true;
    public virtual int EstimatedSchemaTokens => 75;

    protected abstract string DownstreamService { get; }
    protected abstract string HttpMethod { get; }

    public Task<AiToolExecutionOutcome> ExecuteAsync(
        AiToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RestaurantId == Guid.Empty)
        {
            throw new ForbiddenException("RestaurantId is required to generate an action proposal.");
        }

        string relativePath = BuildRelativePath(context);
        string parametersJson = BuildParametersJson(context);
        string summary = BuildSummary(context);
        string impactNotice = BuildImpactNotice(context);

        ActionProposalDto proposal = signer.CreateSignedProposal(
            restaurantId: context.RestaurantId,
            userId: context.UserId,
            actionName: ToolName,
            downstreamService: DownstreamService,
            httpMethod: HttpMethod,
            relativePath: relativePath,
            parametersJson: parametersJson,
            summary: summary,
            impactNotice: impactNotice);

        return Task.FromResult(new AiToolExecutionOutcome(
            IsWriteProposal: true,
            ToolName: ToolName,
            ReadResultJson: null,
            Proposal: proposal));
    }

    protected static string GetArg(AiToolExecutionContext context, string key, string fallback) =>
        context.Arguments.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    protected abstract string BuildRelativePath(AiToolExecutionContext context);

    protected abstract string BuildParametersJson(AiToolExecutionContext context);

    protected abstract string BuildSummary(AiToolExecutionContext context);

    protected abstract string BuildImpactNotice(AiToolExecutionContext context);
}

public sealed class ToggleItem86ProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "ToggleItem86ProposalPlugin";
    public override string ToolName => "toggle_item_86";
    public override string Description => "Proposes toggling 86 (out-of-stock) availability on a menu item.";
    protected override string DownstreamService => "Catalog";
    protected override string HttpMethod => "PATCH";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        string itemId = GetArg(context, "menuItemId", "101");
        return $"/v1/restaurants/{context.RestaurantId}/menu-items/{Uri.EscapeDataString(itemId)}/availability";
    }

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            menuItemId = GetArg(context, "menuItemId", "101"),
            itemName = GetArg(context, "itemName", "Ribeye Steak"),
            isAvailable = bool.TryParse(GetArg(context, "isAvailable", "false"), out bool avail) && avail
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Toggle 86 status on {GetArg(context, "itemName", "Ribeye Steak")}";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Immediately updates POS ordering availability and broadcasts 86 alert to all active KDS stations.";
}

public sealed class BumpTicketProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "BumpTicketProposalPlugin";
    public override string ToolName => "bump_kitchen_ticket";
    public override string Description => "Proposes bumping a completed kitchen ticket off the KDS pass.";
    protected override string DownstreamService => "Kitchen";
    protected override string HttpMethod => "POST";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        string ticketId = GetArg(context, "ticketId", "00000000-0000-0000-0000-000000000001");
        return $"/v1/kitchen/tickets/{Uri.EscapeDataString(ticketId)}/bump";
    }

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            ticketId = GetArg(context, "ticketId", "00000000-0000-0000-0000-000000000001")
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Bump KDS Ticket #{GetArg(context, "ticketId", "00000000-0000-0000-0000-000000000001")}";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Marks all items on the ticket as expedited and notifies floor runners.";
}

public sealed class CreateOrderProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "CreateOrderProposalPlugin";
    public override string ToolName => "create_order";
    public override string Description => "Proposes creating a new dine-in or takeout order for a table.";
    protected override string DownstreamService => "Ordering";
    protected override string HttpMethod => "POST";

    protected override string BuildRelativePath(AiToolExecutionContext context) => "/v1/orders";

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            tableNumber = int.TryParse(GetArg(context, "tableNumber", "4"), out int tableNo) ? tableNo : 4,
            orderType = GetArg(context, "orderType", "DineIn"),
            itemsSummary = GetArg(context, "items", "2x Wagyu Smash Burger, 1x Crispy Calamari")
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Create dine-in order for Table {GetArg(context, "tableNumber", "4")}";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Submits order to Ordering.API and fires course-1 prep tickets to Grill & Fry KDS stations.";
}

public sealed class ApplyDiscountProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "ApplyDiscountProposalPlugin";
    public override string ToolName => "apply_discount";
    public override string Description => "Proposes applying a promotional or comp discount to an open order.";
    protected override string DownstreamService => "Ordering";
    protected override string HttpMethod => "POST";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        string orderId = GetArg(context, "orderId", "00000000-0000-0000-0000-000000000010");
        return $"/v1/orders/{Uri.EscapeDataString(orderId)}/discounts";
    }

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            orderId = GetArg(context, "orderId", "00000000-0000-0000-0000-000000000010"),
            discountPercent = decimal.TryParse(GetArg(context, "discountPercent", "15"), out decimal pct) ? pct : 15m,
            reason = GetArg(context, "reason", "Guest hospitality courtesy")
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Apply {GetArg(context, "discountPercent", "15")}% discount to Order";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Recalculates order subtotal and logs discount in shift audit ledger.";
}

public sealed class UpdateMenuItemProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "UpdateMenuItemProposalPlugin";
    public override string ToolName => "update_menu_item";
    public override string Description => "Proposes updating a menu item's price or description in Catalog.API.";
    protected override string DownstreamService => "Catalog";
    protected override string HttpMethod => "PUT";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        string itemId = GetArg(context, "menuItemId", "101");
        return $"/v1/restaurants/{context.RestaurantId}/menu-items/{Uri.EscapeDataString(itemId)}";
    }

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            menuItemId = GetArg(context, "menuItemId", "101"),
            itemName = GetArg(context, "itemName", "Ribeye Steak"),
            basePrice = decimal.TryParse(GetArg(context, "basePrice", "38.00"), out decimal price) ? price : 38.00m
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Update menu item {GetArg(context, "itemName", "Ribeye Steak")} price to ${GetArg(context, "basePrice", "38.00")}";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Updates live POS catalog pricing for new orders.";
}

public sealed class StaffRoleProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "StaffRoleProposalPlugin";
    public override string ToolName => "update_staff_role";
    public override string Description => "Proposes updating a restaurant staff member's role assignment.";
    protected override string DownstreamService => "Identity";
    protected override string HttpMethod => "PUT";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        string targetUserId = GetArg(context, "targetUserId", "00000000-0000-0000-0000-000000000099");
        return $"/v1/restaurants/{context.RestaurantId}/staff/{Uri.EscapeDataString(targetUserId)}/role";
    }

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            targetUserId = GetArg(context, "targetUserId", "00000000-0000-0000-0000-000000000099"),
            newRole = GetArg(context, "newRole", "KitchenManager")
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Assign role '{GetArg(context, "newRole", "KitchenManager")}' to staff member";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Modifies RBAC permissions on the staff member's next token refresh.";
}

public sealed class QuotaAdminProposalPlugin(IActionProposalSigner signer)
    : ProposalWriteToolPluginBase(signer)
{
    public override string PluginName => "QuotaAdminProposalPlugin";
    public override string ToolName => "configure_ai_safety_ceiling";
    public override string Description => "Proposes enabling or adjusting the restaurant's monthly AI overage safety ceiling.";
    protected override string DownstreamService => "Assistant";
    protected override string HttpMethod => "PUT";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/restaurants/{context.RestaurantId}/quota/config";

    protected override string BuildParametersJson(AiToolExecutionContext context) =>
        JsonSerializer.Serialize(new
        {
            restaurantId = context.RestaurantId,
            allowOverage = bool.TryParse(GetArg(context, "allowOverage", "true"), out bool allow) && allow,
            maxOverageTokens = long.TryParse(GetArg(context, "maxOverageTokens", "250000"), out long tokens) ? tokens : 250_000L,
            maxOverageSpendUsd = decimal.TryParse(GetArg(context, "maxOverageSpendUsd", "50.00"), out decimal usd) ? usd : 50.00m
        });

    protected override string BuildSummary(AiToolExecutionContext context) =>
        $"Adjust AI Assistant monthly safety ceiling to ${GetArg(context, "maxOverageSpendUsd", "50.00")}";

    protected override string BuildImpactNotice(AiToolExecutionContext context) =>
        "Updates Redis and PostgreSQL tenant AI overage cap immediately upon confirmation.";
}
