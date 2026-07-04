namespace Kitchen.API.Hubs;

/// <summary>
/// Typed client contract for the <see cref="KitchenHub"/>. Adds
/// <c>TicketAccepted</c> + <c>TicketRecalled</c> for the transitions the
/// plan lists as local-only state changes but the UI still needs to react to.
/// </summary>
public interface IKitchenHubClient
{
    /// <summary>A new <c>KitchenTicket</c> arrived for this restaurant.</summary>
    Task OrderReceived(KitchenTicketDto ticket);

    /// <summary>Ticket moved <c>New</c> → <c>InProgress</c>.</summary>
    Task TicketAccepted(Guid ticketId, Guid acceptedByUserId, Instant acceptedAt);

    /// <summary>Per-item prep state changed. <paramref name="newStatus"/> is one of
    /// <c>"Pending"</c>, <c>"Preparing"</c>, or <c>"Ready"</c>.</summary>
    Task ItemStateChanged(Guid ticketId, Guid itemId, string newStatus);

    /// <summary>Ticket reached <c>Ready</c>.</summary>
    Task OrderReady(Guid ticketId, Instant readyAt);

    /// <summary>Ticket moved to <c>Bumped</c> (expo acknowledged).</summary>
    Task OrderBumped(Guid ticketId, Instant bumpedAt);

    /// <summary>Ticket was cancelled.</summary>
    Task OrderCancelled(Guid ticketId, string reason);

    /// <summary>Chef pulled a bumped ticket back to <c>Ready</c>.</summary>
    Task TicketRecalled(Guid ticketId);
}