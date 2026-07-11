namespace Catalog.API.Exceptions;

/// <summary>
/// Raised by the catalog cache drift-repair hosted service when a tick fails
/// to repopulate one or more cache keys. Caught and logged at <c>Error</c>
/// level — never rethrown — because cache drift is non-fatal and the next tick
/// will retry.
/// </summary>
/// <remarks>
/// Extends <see cref="BuildingBlocks.Exceptions.InternalServerException"/> so
/// the global <c>CustomExceptionHandler</c> would return 500 if the exception
/// ever escaped the hosted service (defence-in-depth). The original cause is
/// preserved via <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class CacheRepairFailedException(string message, Exception innerException)
    : InternalServerException(message, innerException.Message)
{
    /// <summary>
    /// The exception that caused the cache repair tick to fail. Surfaces on
    /// <see cref="Exception.InnerException"/> for log enrichment.
    /// </summary>
    public new Exception InnerException { get; } = innerException;
}