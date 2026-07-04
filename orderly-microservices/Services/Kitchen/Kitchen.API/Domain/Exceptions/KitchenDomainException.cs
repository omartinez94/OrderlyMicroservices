namespace Kitchen.API.Domain.Exceptions;

/// <summary>
/// Base exception for any business-rule violation in the Kitchen domain.
/// Mapped to HTTP 422 by the global exception handler; subclasses may
/// override the default status (see <see cref="InvalidKitchenTicketStateTransitionException"/>
/// which maps to 409 Conflict).
/// </summary>
public class KitchenDomainException(string message, string paramName)
    : Exception($"Domain exception: {message} throws from Kitchen Domain Layer. (Parameter: {paramName})")
{
}