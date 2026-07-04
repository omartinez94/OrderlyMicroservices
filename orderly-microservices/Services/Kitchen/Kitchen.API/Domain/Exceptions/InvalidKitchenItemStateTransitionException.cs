namespace Kitchen.API.Domain.Exceptions;

/// <summary>
/// Thrown when a <c>KitchenTicketItem</c> transition is invoked against an
/// item in a state that does not permit it (e.g. <c>MarkReady</c> on a
/// <c>Pending</c> item that hasn't been started). Mapped to HTTP 409.
/// </summary>
public class InvalidKitchenItemStateTransitionException(
    Enums.KitchenItemStatus from,
    string attemptedTransition)
    : KitchenDomainException(
        $"Cannot transition KitchenTicketItem from {from} via '{attemptedTransition}'.",
        nameof(attemptedTransition))
{
    public Enums.KitchenItemStatus FromStatus { get; } = from;
    public string AttemptedTransition { get; } = attemptedTransition;
}