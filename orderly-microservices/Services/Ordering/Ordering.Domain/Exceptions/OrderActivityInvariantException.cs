namespace Ordering.Domain.Exceptions;

/// <summary>
/// Thrown when an <see cref="Models.OrderActivity"/> factory or
/// <c>Order.RecordActivity</c> call violates an invariant: unknown enum
/// value, oversize free-text, or null aggregate reference. Mapped to
/// HTTP 422 Unprocessable Content by the global exception handler — the
/// request is well-formed but the activity payload cannot be persisted.
/// </summary>
public class OrderActivityInvariantException(string message)
    : DomainException(message, nameof(OrderActivity))
{ }