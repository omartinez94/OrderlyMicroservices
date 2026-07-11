namespace Catalog.API.Application.Abstractions;

/// <summary>
/// HTTP-context backed <see cref="ICurrentUser"/>. Reads the user id
/// from the JWT <c>sub</c> claim (matches <see cref="JwtClaimExtensions.UserId"/>).
/// Falls back to <c>null</c> when the claim is absent (anonymous endpoint)
/// or not parseable as a <see cref="Guid"/>.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return null;
            }

            var id = user.GetUserId();
            return id == Guid.Empty ? null : id;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}