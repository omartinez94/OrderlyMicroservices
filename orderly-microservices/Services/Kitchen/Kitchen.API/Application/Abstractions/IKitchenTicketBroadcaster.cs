namespace Kitchen.API.Application.Abstractions;

/// <summary>
/// Service contract for broadcasting ticket updates to connected KDS clients over SignalR.
/// </summary>
public interface IKitchenTicketBroadcaster
{
    Task BroadcastTicketUpdatedAsync(KitchenTicket ticket, CancellationToken cancellationToken = default);
}
