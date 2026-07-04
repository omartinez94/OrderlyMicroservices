using System.Security.Claims;

namespace Kitchen.API.Infrastructure;

/// <summary>
/// Resolves <see cref="ICurrentUser"/> from <see cref="IHttpContextAccessor"/>.
/// Reads the standard <c>ClaimTypes.NameIdentifier</c> which Identity populates
/// with the user's <see cref="Guid"/>. Returns <c>null</c> on anonymous
/// requests so the same handler can short-circuit cleanly when the command is
/// invoked through a non-HTTP path (tests, replay tooling).
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;

            var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => accessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
}