using Marten.Schema;

namespace Basket.API.Models;

/// <summary>
/// The cart document. Implements <see cref="ITenantEntity"/> so the
/// Marten <c>MultiTenanted()</c> registration in <c>Program.cs</c> tags
/// every row with the current restaurant id; per-request reads/writes
/// are filtered by <see cref="ICurrentRestaurantProvider"/> so a
/// caller cannot reach across tenants.
/// </summary>
public class Basket : ITenantEntity
{
    public Basket()
    {
    }

    public Basket(Guid userId, Guid restaurantId)
    {
        UserId = userId;
        RestaurantId = restaurantId;
    }

    [Identity]
    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<BasketItem> Items { get; set; } = [];

    /// <summary>
    /// User-input coupon codes (the strings the cart UI submits).
    /// Survives Phase 2.2 — the per-coupon breakdown lives in
    /// <see cref="AppliedCoupons"/> alongside. The wire shape keeps
    /// the string list for backwards compatibility with the existing
    /// <c>BasketCheckoutEvent</c> payload (Phase 2.1 will replace it
    /// with a structured <c>CouponSnapshot[]</c>).
    /// </summary>
    public List<string> AppliedDiscounts { get; set; } = [];

    /// <summary>
    /// Per-coupon breakdown. Populated by <c>StoreBasketHandler</c> on
    /// every PUT; each entry mirrors what the Discount.Grpc <c>GetDiscount</c>
    /// RPC returned at upsert time. Each <see cref="CouponSnapshot.DiscountAmount"/>
    /// is the coupon's contribution **unclamped** to the cart subtotal —
    /// the basket-level <see cref="DiscountAmount"/> is the clamp.
    /// </summary>
    public List<CouponSnapshot> AppliedCoupons { get; set; } = [];

    /// <summary>
    /// Server-computed sum of <see cref="AppliedCoupons"/> discounts,
    /// clamped to <see cref="Subtotal"/> so the total can never go
    /// negative. Populated by <c>StoreBasketHandler</c>; carried into
    /// <c>BasketCheckoutEvent.TotalAmount</c> as
    /// <c>Subtotal - DiscountAmount</c>.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    public decimal Subtotal => Items.Sum(x => x.TotalPrice);

    /// <summary>Derived — the user-visible cart total. Not stored.</summary>
    public decimal Total => Math.Max(Subtotal - DiscountAmount, 0m);

    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }
}

/// <summary>
/// Per-coupon breakdown populated by <c>StoreBasketHandler</c>. The
/// fields mirror the wire shape of <c>Discount.Grpc.CouponModel</c>
/// as resolved at upsert time, plus the basket-local
/// <see cref="DiscountAmount"/> (the coupon's contribution to the
/// cart total) and <see cref="AppliedAt"/>.
/// </summary>
/// <remarks>
/// Replaces the Phase 2 wire-shape <c>List&lt;string&gt;</c> coupon
/// list for the v2 <c>BasketCheckoutEvent</c> payload
/// (Phase 2.1, BuildingBlocks contribution). For Phase 2.2 the
/// snapshot lives only on the <see cref="Basket"/> document;
/// <c>BasketCheckoutEvent</c> still carries <c>AppliedDiscounts</c>
/// as strings.
/// </remarks>
public record CouponSnapshot(
    string Code,
    string Description,
    decimal DiscountAmount,
    Instant AppliedAt);
