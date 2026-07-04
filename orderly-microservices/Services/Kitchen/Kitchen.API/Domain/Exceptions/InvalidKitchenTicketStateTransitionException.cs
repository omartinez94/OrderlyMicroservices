namespace Kitchen.API.Domain.Exceptions;

/// <summary>
/// Thrown when a <c>KitchenTicket</c> behaviour method is invoked against an
/// aggregate whose <c>Status</c> does not permit the transition (e.g.
/// <c>MarkReady</c> on a <c>New</c> ticket). Mapped to HTTP 409 Conflict by
/// the global exception handler — the request is well-formed but conflicts
/// with current state.
/// </summary>
public class InvalidKitchenTicketStateTransitionException(
    Enums.KitchenTicketStatus from,
    string attemptedTransition)
    : KitchenDomainException(
        $"Cannot transition KitchenTicket from {from} via '{attemptedTransition}'.",
        nameof(attemptedTransition))
{
    public Enums.KitchenTicketStatus FromStatus { get; } = from;
    public string AttemptedTransition { get; } = attemptedTransition;
}