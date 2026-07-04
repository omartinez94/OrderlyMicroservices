namespace Kitchen.API.Domain.Enums;

/// <summary>
/// Per-item preparation state. A <c>KitchenTicket</c> cannot move to
/// <see cref="KitchenTicketStatus.Ready"/> until every item is
/// <see cref="Ready"/> — see <c>KitchenTicket.MarkReady</c>.
/// </summary>
public enum KitchenItemStatus
{
    Pending = 0,
    Preparing = 1,
    Ready = 2
}