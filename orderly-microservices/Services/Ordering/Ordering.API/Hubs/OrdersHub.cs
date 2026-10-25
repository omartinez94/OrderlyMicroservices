using System.Security.Claims;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.FeatureManagement;
using NodaTime;
using Ordering.API.Observability;

namespace Ordering.API.Hubs;

[Authorize]
public sealed class OrdersHub(
    IClock clock,
    IFeatureManager featureManager,
    OrderingLiveMetrics metrics,
    ILogger<OrdersHub> logger)
    : Hub<IOrdersHubClient>
{
    public const string FeatureFlagName = "OrdersLiveView";

    public static string RestaurantGroup(Guid restaurantId) => $"restaurant:{restaurantId}";

    public static string StatsGroup(Guid restaurantId) => $"restaurant:{restaurantId}:stats";

    public static string RevenueGroup(Guid restaurantId) => $"restaurant:{restaurantId}:revenue";

    public override async Task OnConnectedAsync()
    {
        if (!await IsFeatureEnabledAsync())
        {
            logger.LogInformation(
                "OrdersHub connection {ConnectionId} rejected because feature flag {FeatureFlag} is disabled.",
                Context.ConnectionId,
                FeatureFlagName);
            Context.Abort();
            return;
        }

        List<Guid> restaurantIds = ResolveRestaurantIds(Context.User);
        if (restaurantIds.Count == 0)
        {
            logger.LogWarning(
                "OrdersHub connection {ConnectionId} rejected: missing restaurantId claim.",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();

        bool canViewAll = HasPermissionOrSuperAdmin(Context.User, "orders:view_all");
        bool canViewReports = HasPermissionOrSuperAdmin(Context.User, "payments:view_reports");

        foreach (Guid restaurantId in restaurantIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

            if (canViewAll)
            {
                string statsGroup = canViewReports
                    ? RevenueGroup(restaurantId)
                    : StatsGroup(restaurantId);
                await Groups.AddToGroupAsync(Context.ConnectionId, statsGroup);
            }
        }

        metrics.OnClientConnected();
        logger.LogInformation(
            "OrdersHub connection {ConnectionId} joined {Count} restaurant group(s) (viewAll={CanViewAll}, viewReports={CanViewReports}).",
            Context.ConnectionId,
            restaurantIds.Count,
            canViewAll,
            canViewReports);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        metrics.OnClientDisconnected();
        return base.OnDisconnectedAsync(exception);
    }

    public long GetServerTime() => clock.GetCurrentInstant().ToUnixTimeMilliseconds();

    private async Task<bool> IsFeatureEnabledAsync()
    {
        bool defined = false;
        await foreach (string name in featureManager.GetFeatureNamesAsync())
        {
            if (string.Equals(name, FeatureFlagName, StringComparison.OrdinalIgnoreCase))
            {
                defined = true;
                break;
            }
        }

        if (!defined)
        {
            return true;
        }

        return await featureManager.IsEnabledAsync(FeatureFlagName);
    }

    private static List<Guid> ResolveRestaurantIds(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return [];
        }

        List<Guid> ids = [];
        foreach (Claim claim in principal.Claims)
        {
            if (claim.Type is "restaurantId" or "restaurantIds" &&
                Guid.TryParse(claim.Value, out Guid parsed) &&
                parsed != Guid.Empty &&
                !ids.Contains(parsed))
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }

    private static bool HasPermissionOrSuperAdmin(ClaimsPrincipal? principal, string permission)
    {
        if (principal is null)
        {
            return false;
        }

        if (principal.HasPermission(permission))
        {
            return true;
        }

        foreach (Claim claim in principal.Claims)
        {
            if ((claim.Type == ClaimTypes.Role || claim.Type == "role") &&
                string.Equals(claim.Value, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
