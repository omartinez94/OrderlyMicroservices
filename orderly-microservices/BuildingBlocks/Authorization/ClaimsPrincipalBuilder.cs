using System.Security.Claims;

namespace BuildingBlocks.Authorization;

/// <summary>
/// Fluent builder for <see cref="ClaimsPrincipal"/> instances used outside the
/// HTTP request pipeline — primarily by MassTransit consumers operating under
/// Pattern 2 (synthetic claims from event payloads, per the Discount plan §0.4.5
/// / §6.5 row). The same shape works for any other scope where a principal must
/// be minted from non-HTTP input (background jobs, test fixtures, scheduled
/// hosted services that need to act on behalf of a tenant).
/// </summary>
/// <remarks>
/// <para>
/// The builder is a thin layer over <see cref="ClaimsIdentity"/> that puts the
/// project-wide claim names (<c>restaurantId</c>, <c>permissions</c>,
/// <see cref="ClaimTypes.NameIdentifier"/>, <see cref="ClaimTypes.Name"/>) on
/// equal footing. Without this builder, every consumer that wants a synthetic
/// principal re-implements the claim-mapping from scratch, which is how the
/// JWT claim-shape drift (Shape A vs Shape B in the Discount v1.2 changelog
/// M-L8) creeps back in.
/// </para>
/// <para>
/// <see cref="WithPermission(string[])"/> emits each permission under the
/// <c>permissions</c> claim type. That matches Identity's canonical emission
/// (one claim per granted permission — Shape A). Consumers that read with
/// the comma-split Shape B must split the value themselves; this builder
/// does not pre-split.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var principal = new ClaimsPrincipalBuilder()
///     .WithRestaurant(restaurantId)
///     .WithUser(userId)
///     .WithActor("discount-service")
///     .WithPermission("coupon:read", "coupon:redeem")
///     .Build();
///
/// using (tenant.Attach(principal))
/// {
///     // Bus-event scope — tenant provider now serves restaurantId.
/// }
/// </code>
/// </example>
public sealed class ClaimsPrincipalBuilder
{
    private readonly List<Claim> _claims = [];
    private string _authType = "DiscountSynthetic";

    /// <summary>
    /// Sets the underlying <see cref="ClaimsIdentity.AuthenticationType"/>.
    /// Defaults to <c>DiscountSynthetic</c>. Pass an empty string for an
    /// unauthenticated principal (rare; useful for negative-path tests).
    /// </summary>
    public ClaimsPrincipalBuilder WithAuthType(string authType)
    {
        _authType = authType;
        return this;
    }

    /// <summary>
    /// Adds the <c>restaurantId</c> claim used by
    /// <see cref="ICurrentRestaurantProvider"/> to scope the global
    /// query filter. Stores the GUID as a string for round-trip stability
    /// with <c>JwtClaimExtensions.GetRestaurantId</c>.
    /// </summary>
    public ClaimsPrincipalBuilder WithRestaurant(Guid restaurantId)
    {
        _claims.Add(new Claim("restaurantId", restaurantId.ToString()));
        return this;
    }

    /// <summary>
    /// Adds the <see cref="ClaimTypes.NameIdentifier"/> claim (the canonical
    /// user-id slot). Pairs with <see cref="JwtClaimExtensions.GetUserId"/>.
    /// </summary>
    public ClaimsPrincipalBuilder WithUser(Guid userId)
    {
        _claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        return this;
    }

    /// <summary>
    /// Adds the <see cref="ClaimTypes.Name"/> claim with the actor string
    /// (e.g., <c>"discount-service"</c> for bus-triggered consumers or
    /// <c>"discount-system"</c> for hosted-service actions). The
    /// <see cref="AuditableEntityInterceptor"/> reads this for audit columns
    /// on entities persisted by the consumer.
    /// </summary>
    public ClaimsPrincipalBuilder WithActor(string actor)
    {
        _claims.Add(new Claim(ClaimTypes.Name, actor));
        return this;
    }

    /// <summary>
    /// Adds one <c>permissions</c> claim per granted permission string
    /// (Identity emits Shape A — one claim each). Consumers using the
    /// comma-split Shape B must split the value themselves.
    /// </summary>
    public ClaimsPrincipalBuilder WithPermission(params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            _claims.Add(new Claim("permissions", permission));
        }
        return this;
    }

    /// <summary>
    /// Adds a <c>correlationId</c> claim carrying the trace identifier. Used
    /// for log-scope stitching between the publishing service and the
    /// bus-triggered consumer.
    /// </summary>
    public ClaimsPrincipalBuilder WithCorrelationId(string correlationId)
    {
        _claims.Add(new Claim("correlationId", correlationId));
        return this;
    }

    /// <summary>
    /// Builds a <see cref="ClaimsPrincipal"/> wrapping a
    /// <see cref="ClaimsIdentity"/> with the accumulated claims and the
    /// configured <see cref="ClaimsIdentity.AuthenticationType"/>. Returns
    /// a freshly-allocated principal — no caching — so callers may freely
    /// pass the result to <c>Attach</c> on the tenant provider without
    /// worrying about identity-state corruption.
    /// </summary>
    public ClaimsPrincipal Build()
    {
        var identity = new ClaimsIdentity(_claims, _authType);
        return new ClaimsPrincipal(identity);
    }
}
