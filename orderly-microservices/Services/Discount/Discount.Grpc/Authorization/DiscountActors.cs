namespace Discount.Grpc.Authorization;

/// <summary>
/// Centralized audit-actor strings for the Discount service. The
/// <c>AuditableEntityInterceptor</c> writes one of these constants into
/// <see cref="BuildingBlocks.Entities.Contracts.AuditableEntity.LastModifiedBy"/>
/// for every Discount-driven mutation. Centralizing prevents drift between
/// inline string literals (today's sweep service hardcodes
/// <c>"discount-sweep"</c>, the conditional-UPDATE in
/// <c>RedeemDiscount</c> hardcodes <c>"discount-system"</c>) and gives the bus
/// consumer attachment point one place to mint the actor for
/// synthetic principals.
/// </summary>
/// <remarks>
/// <para>The three constants map to the three actors that mutate Discount
/// aggregates today:</para>
/// <list type="bullet">
/// <item><see cref="System"/> — the conditional UPDATE in
/// <see cref="Services.DiscountService.RedeemDiscount"/>. The raw SQL bypasses
/// <c>AuditableEntityInterceptor</c>, so the constant must be passed in
/// explicitly.</item>
/// <item><see cref="Sweep"/> — the <see cref="Services.DiscountExpirySweepService"/>
/// host; soft-deletes expired coupons.</item>
/// <item><see cref="Service"/> — MassTransit consumers (
/// <c>OrderCreatedConsumer</c> stub) acting on behalf of the Discount service.
/// Minted via <see cref="BuildingBlocks.Authorization.ClaimsPrincipalBuilder.WithActor"/>.</item>
/// </list>
/// </remarks>
public static class DiscountActors
{
    /// <summary>
    /// Actor string for the atomic conditional-UPDATE path in
    /// <see cref="Services.DiscountService.RedeemDiscount"/>. Distinct from
    /// <see cref="Sweep"/> so audit logs can separate operator-driven
    /// soft-deletes from system-driven counter increments.
    /// </summary>
    public const string System = "discount-system";

    /// <summary>
    /// Actor string for the <see cref="Services.DiscountExpirySweepService"/>
    /// hosted service. Reserved exclusively for sweep-driven soft-deletes.
    /// </summary>
    public const string Sweep = "discount-sweep";

    /// <summary>
    /// Actor string for MassTransit consumers acting on behalf of Discount
    /// (<c>FeedbackSubmittedConsumer</c>,
    /// <c>OrderCreatedConsumer</c> stub). Used as
    /// <see cref="System.Security.Claims.ClaimTypes.Name"/> via
    /// <see cref="BuildingBlocks.Authorization.ClaimsPrincipalBuilder.WithActor"/>
    /// when constructing Pattern 2 synthetic principals.
    /// </summary>
    public const string Service = "discount-service";
}
