namespace Ordering.Domain.Exceptions;

/// <summary>
/// Thrown when an <c>Order</c> behaviour method is invoked against an
/// aggregate whose <c>Status</c> does not permit the transition (e.g.
/// <c>Confirm</c> on an already <c>Confirmed</c> order). Mapped to HTTP
/// 409 Conflict by the global exception handler — the request is
/// well-formed but conflicts with current state.
/// </summary>
public class InvalidOrderStateTransitionException(
    OrderStatus from,
    string attemptedTransition)
    : DomainException(
        $"Cannot transition Order from {from} via '{attemptedTransition}'.",
        nameof(attemptedTransition))
{
    public OrderStatus FromStatus { get; } = from;
    public string AttemptedTransition { get; } = attemptedTransition;
}