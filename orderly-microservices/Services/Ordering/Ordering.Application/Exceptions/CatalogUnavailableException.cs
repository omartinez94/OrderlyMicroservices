namespace Ordering.Application.Exceptions;

using Ordering.Domain.Exceptions;

public class CatalogUnavailableException : DomainException
{
    public int RetryAfterSeconds { get; }

    public CatalogUnavailableException(string message = "The catalog service is temporarily unavailable. Please retry shortly.", int retryAfterSeconds = 5)
        : base(message, "Catalog")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}