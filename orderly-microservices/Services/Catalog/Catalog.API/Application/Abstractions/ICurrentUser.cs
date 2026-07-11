namespace Catalog.API.Application.Abstractions;

/// <summary>
/// Reads the authenticated staff member from the current HTTP request.
/// Used by command handlers to stamp <c>UploadedByUserId</c> /
/// <c>ChangedByUserId</c> columns on Catalog-owned audit tables
/// (<c>BulkOrderUpload</c>, <c>PriceHistory</c>). Implementation lives in
/// <c>HttpContextCurrentUser</c> because it depends on
/// <c>IHttpContextAccessor</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's <see cref="Guid"/> id, or <c>null</c> if the request is anonymous.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}