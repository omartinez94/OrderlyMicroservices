namespace Kitchen.API.Application.Abstractions;

/// <summary>
/// Reads the authenticated staff member from the current HTTP request. Used
/// by command handlers to stamp <c>AcceptedByUserId</c> /
/// <c>CancelledByUserId</c> columns and to embed the user id in outbound
/// integration events. Implementation lives in the API composition root
/// (<c>HttpContextCurrentUser</c>) because it depends on
/// <c>IHttpContextAccessor</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's <see cref="Guid"/> id, or <c>null</c> if the request is anonymous.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}