using Microsoft.AspNetCore.Authorization;

namespace Kitchen.API.Hubs;

/// <summary>
/// The kitchen-side SignalR hub. Single route <c>/hubs/kitchen</c> behind
/// <c>[Authorize]</c> so every connection carries a JWT validated by the
/// Identity authority (see <see cref="Authorization"/>). Clients negotiate
/// with <c>?access_token=...</c>; the SignalR client library hoists the
/// token onto the upgrade request and the same JWT validation middleware
/// applies.
///
/// On connect, the user's <c>restaurantIds</c> claims (populated by Identity's
/// <c>ClaimsTransformer</c>) auto-join them to <c>restaurant:{id}</c> groups
/// so broadcasts from the <see cref="Application.EventHandlers.Domain.KitchenTicketBroadcaster"/>
/// reach every staff user subscribed to that restaurant. Stations are joined
/// explicitly via <see cref="JoinStationGroup"/> when the UI wants a
/// per-station view.
/// </summary>
[Authorize]
public class KitchenHub : Hub<IKitchenHubClient>
{
    private readonly ILogger<KitchenHub> _logger;

    public KitchenHub(ILogger<KitchenHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var restaurantIds = Context.User?
            .FindAll("restaurantIds")
            .Select(c => c.Value)
            .Where(s => Guid.TryParse(s, out _))
            .Select(s => Guid.Parse(s))
            .Distinct()
            .ToList() ?? new List<Guid>();

        foreach (var id in restaurantIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"restaurant:{id}");
        }

        if (restaurantIds.Count > 0)
        {
            _logger.LogInformation(
                "KitchenHub connection {ConnectionId} joined {Count} restaurant group(s).",
                Context.ConnectionId, restaurantIds.Count);
        }
    }

    /// <summary>Adds the caller to a <c>station:{id}</c> group for station-scoped broadcasts.</summary>
    public async Task JoinStationGroup(Guid stationId)
    {
        if (stationId == Guid.Empty)
            throw new HubException("stationId must be non-empty.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"station:{stationId}");
    }

    /// <summary>Removes the caller from a <c>station:{id}</c> group.</summary>
    public async Task LeaveStationGroup(Guid stationId)
    {
        if (stationId == Guid.Empty)
            throw new HubException("stationId must be non-empty.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"station:{stationId}");
    }

    /// <summary>Optional server-side method for typing-indicator / ACK flows. Not used today.</summary>
    public Task Acknowledge(string clientRequestId) => Task.CompletedTask;
}