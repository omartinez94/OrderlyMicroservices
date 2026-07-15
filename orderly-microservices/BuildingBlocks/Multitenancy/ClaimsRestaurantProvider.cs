using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Multitenancy;

/// <summary>
/// Reads the tenant from the <c>restaurantId</c> claim on the current
/// <see cref="HttpContext.User"/>. Returns <see cref="Guid.Empty"/> when:
///   - there is no HTTP context (background services, design-time tooling), or
///   - the user is unauthenticated, or
///   - the claim is missing or not a valid <see cref="Guid"/>.
/// Returning <c>Guid.Empty</c> causes the global query filter to match no rows
/// — the fail-secure default. The tenant provider is intentionally the single
/// source of truth for tenant identity in a request scope; downstream code
/// reads from <see cref="ICurrentRestaurantProvider"/> only.
/// </summary>
/// <remarks>
/// <para>Pattern 2 (synthetic claims for bus-triggered consumers) is supported
/// through <see cref="Attach"/>. The returned <see cref="IDisposable"/> pushes
/// the attached principal onto an <see cref="AsyncLocal{T}"/> stack; while
/// the scope is active, <see cref="RestaurantId"/> reads the attached
/// principal's <c>restaurantId</c> claim instead of the HTTP context. When the
/// scope ends the prior HTTP-scope behaviour is restored automatically.</para>
/// <para>The <see cref="AsyncLocal{T}"/> flows through async hops so a bus
/// consumer can hand work to a hosted service without losing the tenant
/// context. Test fixtures use the same primitive to drive tenant-scoped
/// tests without spinning up an HTTP pipeline.</para>
/// </remarks>
public sealed class ClaimsRestaurantProvider(IHttpContextAccessor accessor) : ICurrentRestaurantProvider
{
    private static readonly AsyncLocal<ClaimsPrincipal?> _attached = new();

    /// <inheritdoc />
    public Guid RestaurantId
    {
        get
        {
            // Attached principal wins (Pattern 2 / bus-triggered consumer scope).
            var attached = _attached.Value;
            if (attached?.Identity?.IsAuthenticated == true)
            {
                var raw = attached.FindFirstValue("restaurantId");
                if (Guid.TryParse(raw, out var attachedId))
                {
                    return attachedId;
                }
            }

            var context = accessor.HttpContext;
            if (context?.User?.Identity?.IsAuthenticated != true)
            {
                return Guid.Empty;
            }

            var httpRaw = context.User.FindFirstValue("restaurantId");
            return Guid.TryParse(httpRaw, out var httpId) ? httpId : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public IDisposable Attach(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var prior = _attached.Value;
        _attached.Value = principal;
        return new Scope(prior);
    }

    private sealed class Scope(ClaimsPrincipal? prior) : IDisposable
    {
        private ClaimsPrincipal? _prior = prior;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _attached.Value = _prior;
            _prior = null;
            _disposed = true;
        }
    }
}
