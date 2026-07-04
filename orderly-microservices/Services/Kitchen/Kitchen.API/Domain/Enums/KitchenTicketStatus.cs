namespace Kitchen.API.Domain.Enums;

/// <summary>
/// Lifecycle state of a <c>KitchenTicket</c>. The legal transitions are:
/// <code>
/// New         --accept-->  InProgress
/// InProgress  --item-all-ready-->  Ready
/// Ready       --bump-->     Bumped
/// Bumped      --recall-->   Ready
/// Any         --cancel-->   Cancelled
/// </code>
/// Enforced by <see cref="Aggregates.KitchenTicket.KitchenTicket"/>; any
/// illegal transition throws <c>InvalidKitchenTicketStateTransitionException</c>.
/// </summary>
public enum KitchenTicketStatus
{
    New = 0,
    InProgress = 1,
    Ready = 2,
    Bumped = 3,
    Cancelled = 4
}