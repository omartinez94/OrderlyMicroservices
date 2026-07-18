namespace BuildingBlocks.Exceptions;

/// <summary>
/// Thrown when an authenticated caller attempts to act on a resource they
/// are not authorised to touch — cross-tenant reads, cross-user mutations,
/// or admin bypass without the required permission claim. Maps to HTTP
/// 403 Forbidden via <see cref="Handler.CustomExceptionHandler"/>.
/// </summary>
/// <remarks>
/// Distinct from a 401 (no/invalid token): a 403 means the caller
/// authenticated successfully but their identity lacks the permission
/// required for the requested action. BuildingBlocks keeps the two
/// separate so clients can distinguish "log in" from "ask for access".
/// </remarks>
public class ForbiddenException : Exception
{
    /// <summary>
    /// Optional human-readable detail for the client. Surface only when
    /// the caller has a programmatic way to act on it (e.g. the missing
    /// permission name the FE can display).
    /// </summary>
    public string? Description { get; }

    public ForbiddenException(string message = "Forbidden.") : base(message)
    {
    }

    public ForbiddenException(string message, string description) : base(message)
    {
        Description = description;
    }
}