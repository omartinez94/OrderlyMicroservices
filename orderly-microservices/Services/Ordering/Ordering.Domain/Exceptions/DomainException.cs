namespace Ordering.Domain.Exceptions;

public class DomainException(string message, string paramName) : Exception($"Domain exception: {message} throws from Domain Layer. (Parameter: {paramName})")
{
}
