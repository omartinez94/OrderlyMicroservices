using System.Security.Claims;

namespace BuildingBlocks.Multitenancy;

/// <summary>
/// Provides the current restaurant (tenant) identifier for the active request or
/// event-processing scope. Implementations include:
///   - <see cref="ClaimsRestaurantProvider"/> for HTTP / gRPC request scope (Phase 1+3).
///   - A bus-event-derived synthetic provider (Pattern 2 from Q10) for MassTransit consumers (Phase 5).
/// Returns <see cref="Guid.Empty"/> when no tenant can be resolved; the global query
/// filter then matches no rows, which is the fail-secure default.
/// </summary>
public interface ICurrentRestaurantProvider
{
    /// <summary>
    /// Restaurant GUID for the active scope (HTTP request, bus-consume
    /// context, or hosted-service scope). Returns <see cref="Guid.Empty"/>
    /// when no tenant can be resolved.
    /// </summary>
    Guid RestaurantId { get; }

    /// <summary>
    /// Attaches a synthetic <see cref="ClaimsPrincipal"/> for the duration of
    /// the returned <see cref="IDisposable"/> scope. Used by MassTransit
    /// consumers (Pattern 2 from Q10) to feed a tenant context derived from
    /// the inbound integration event payload rather than from an HTTP request.
    /// </summary>
    /// <remarks>
    /// <para>While the scope is active, the provider MUST serve
    /// <see cref="RestaurantId"/> from the attached principal's
    /// <c>restaurantId</c> claim in preference to any ambient HTTP context.
    /// On <c>Dispose</c>, the prior tenant resolution rule (HTTP context)
    /// is restored.</para>
    /// <para>The same implementation works for non-bus scopes (background
    /// services, design-time tooling, test fixtures) that need an
    /// out-of-band tenant context.</para>
    /// </remarks>
    IDisposable Attach(ClaimsPrincipal principal);
}
