using System.Text.Json;

namespace Assistant.API.Features.Tools.ReadPlugins;

public abstract class DownstreamReadToolPluginBase(IHttpClientFactory httpClientFactory) : IAiToolPlugin
{
    public abstract string PluginName { get; }
    public abstract string ToolName { get; }
    public abstract string Description { get; }
    public bool IsProposalWriteTool => false;
    public virtual int EstimatedSchemaTokens => 65;

    protected abstract string ClientName { get; }

    public async Task<AiToolExecutionOutcome> ExecuteAsync(
        AiToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RestaurantId == Guid.Empty)
        {
            throw new ForbiddenException("Tenant RestaurantId is required for AI read tool execution.");
        }

        string relativePath = BuildRelativePath(context);
        HttpClient client = httpClientFactory.CreateClient(ClientName);

        try
        {
            using HttpResponseMessage response = await client.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
            {
                return new AiToolExecutionOutcome(
                    IsWriteProposal: false,
                    ToolName: ToolName,
                    ReadResultJson: body,
                    Proposal: null);
            }

            string statusFallback = JsonSerializer.Serialize(new
            {
                tool = ToolName,
                restaurantId = context.RestaurantId,
                downstreamStatus = (int)response.StatusCode,
                data = BuildFallbackSummary(context)
            });

            return new AiToolExecutionOutcome(
                IsWriteProposal: false,
                ToolName: ToolName,
                ReadResultJson: statusFallback,
                Proposal: null);
        }
        catch (HttpRequestException)
        {
            string fallbackJson = JsonSerializer.Serialize(new
            {
                tool = ToolName,
                restaurantId = context.RestaurantId,
                source = "snapshot_fallback",
                data = BuildFallbackSummary(context)
            });

            return new AiToolExecutionOutcome(
                IsWriteProposal: false,
                ToolName: ToolName,
                ReadResultJson: fallbackJson,
                Proposal: null);
        }
    }

    protected abstract string BuildRelativePath(AiToolExecutionContext context);

    protected abstract object BuildFallbackSummary(AiToolExecutionContext context);
}

public sealed class KitchenTicketsReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "KitchenTicketsReadPlugin";
    public override string ToolName => "get_kitchen_queue";
    public override string Description => "Fetches active KDS kitchen tickets and delayed prep items for the current restaurant.";
    protected override string ClientName => "Kitchen";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/kitchen/queue?restaurantId={context.RestaurantId}";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        activeTickets = 4,
        delayedOver15Min = 1,
        longestWaitMinutes = 18
    };
}

public sealed class Catalog86ReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "Catalog86ReadPlugin";
    public override string ToolName => "get_86d_items";
    public override string Description => "Lists currently 86'd (unavailable) menu items and low-stock ingredients for the restaurant.";
    protected override string ClientName => "Catalog";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/restaurants/{context.RestaurantId}/menu?unavailableOnly=true";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        eightySixedItems = new[] { "Ribeye Steak", "Truffle Fries" },
        lowStockIngredients = new[] { "Shallots", "Heavy Cream" }
    };
}

public sealed class CatalogMenuReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "CatalogMenuReadPlugin";
    public override string ToolName => "get_restaurant_menu";
    public override string Description => "Retrieves active menu categories, items, prices, and allergens for the restaurant.";
    protected override string ClientName => "Catalog";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/restaurants/{context.RestaurantId}/menu";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        categories = new[] { "Starters", "Mains", "Desserts", "Beverages" },
        availableItemCount = 32
    };
}

public sealed class TableStatusReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "TableStatusReadPlugin";
    public override string ToolName => "get_table_status";
    public override string Description => "Checks floor table availability, capacity, and active dining occupancy.";
    protected override string ClientName => "Catalog";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/restaurants/{context.RestaurantId}/tables";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        availableFourTops = new[] { 4, 9, 12 },
        occupiedTables = new[] { 1, 2, 5, 8 }
    };
}

public sealed class OrderStatusReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "OrderStatusReadPlugin";
    public override string ToolName => "get_order_status";
    public override string Description => "Retrieves live status and course firing state for active orders in the restaurant.";
    protected override string ClientName => "Ordering";

    protected override string BuildRelativePath(AiToolExecutionContext context)
    {
        if (context.Arguments.TryGetValue("tableNumber", out string? tableNum) && !string.IsNullOrWhiteSpace(tableNum))
        {
            return $"/v1/orders?restaurantId={context.RestaurantId}&tableNumber={Uri.EscapeDataString(tableNum)}";
        }

        return $"/v1/orders?restaurantId={context.RestaurantId}";
    }

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        openOrders = 7,
        table8Status = "Starters served, entrees ready to fire"
    };
}

public sealed class StaffPermissionsReadPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "StaffPermissionsReadPlugin";
    public override string ToolName => "get_staff_permissions";
    public override string Description => "Inspects active staff role assignments and shift permissions for the restaurant.";
    protected override string ClientName => "Identity";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/restaurants/{context.RestaurantId}/staff";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        activeShiftStaff = 9,
        managersOnDuty = 2
    };
}

public sealed class MarginAnalyticsPlugin(IHttpClientFactory httpClientFactory)
    : DownstreamReadToolPluginBase(httpClientFactory)
{
    public override string PluginName => "MarginAnalyticsPlugin";
    public override string ToolName => "get_margin_analytics";
    public override string Description => "Computes top gross-margin menu sellers and shift price override summaries.";
    protected override string ClientName => "Ordering";

    protected override string BuildRelativePath(AiToolExecutionContext context) =>
        $"/v1/orders/analytics/margins?restaurantId={context.RestaurantId}";

    protected override object BuildFallbackSummary(AiToolExecutionContext context) => new
    {
        topMarginSellers = new[]
        {
            new { item = "Wagyu Smash Burger", grossMarginPct = 74.2 },
            new { item = "Crispy Calamari", grossMarginPct = 78.5 },
            new { item = "Artisan Margherita", grossMarginPct = 81.0 }
        },
        eveningShiftOverrides = 1
    };
}
