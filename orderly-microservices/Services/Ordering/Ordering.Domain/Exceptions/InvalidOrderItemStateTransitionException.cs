namespace Ordering.Domain.Exceptions;

/// <summary>
/// Thrown when an <c>OrderItem</c> behaviour method is invoked against a
/// line item whose <c>PrepStatus</c> does not permit the transition (e.g.
/// <c>MarkItemReady</c> on a still-Pending item). Mapped to HTTP 409 by
/// the global exception handler.
/// </summary>
public class InvalidOrderItemStateTransitionException(
    PrepStatus from,
    string attemptedTransition)
    : DomainException(
        $"Cannot transition OrderItem from {from} via '{attemptedTransition}'.",
        nameof(attemptedTransition))
{
    public PrepStatus FromStatus { get; } = from;
    public string AttemptedTransition { get; } = attemptedTransition;
}