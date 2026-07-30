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
    /// Survives — the per-coupon breakdown lives in
    /// <see cref="AppliedCoupons"/> + <see cref="AppliedDiscountBreakdown"/>
    /// alongside. The wire shape keeps the string list for backwards
    /// compatibility with the existing <c>BasketCheckoutEvent</c>
    /// payload.
    /// </summary>
    public List<string> AppliedDiscounts { get; set; } = [];

    /// <summary>
    /// per-coupon breakdown. Populated by
    /// <c>StoreBasketHandler</c> on every PUT; each entry mirrors what
    /// the Discount.Grpc <c>GetDiscount</c> RPC returned at upsert
    /// time. Each <see cref="CouponSnapshot.DiscountAmount"/> is the
    /// coupon's contribution **unclamped** to the cart subtotal — the
    /// basket-level <see cref="DiscountAmount"/> is the clamp.
    /// </summary>
    /// <remarks>
    /// widens this list with
    /// <see cref="AppliedDiscountBreakdown"/>, which carries the full
    /// <see cref="BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply"/>
    /// output (floor-at-zero + MidpointRounding.ToEven rounding policy
    /// included). The two lists are populated together by the handler.
    /// </remarks>
    public List<CouponSnapshot> AppliedCoupons { get; set; } = [];

    /// <summary>
    /// per-coupon breakdown carrying the full helper output
    /// (CouponId, Code, DiscountType, RequestedAmount, AppliedAmount,
    /// AppliedAt). Embedded as a child list of the Marten document —
    /// no separate table, no FK, no cascade-delete concern. The
    /// parent's <see cref="LastModifiedAt"/> write replaces the doc
    /// atomically. Storing the breakdown lets the UI render a
    /// customer-visible "X% off applied" line per coupon and gives
    /// admins an audit trail of which coupons were active at the
    /// time of the upsert.
    /// </summary>
    public List<BasketAppliedDiscount> AppliedDiscountBreakdown { get; set; } = [];

    /// <summary>
    /// Server-computed sum of <see cref="AppliedCoupons"/> discounts,
    /// clamped to <see cref="Subtotal"/> so the total can never go
    /// negative. Populated by <c>StoreBasketHandler</c>; carried into
    /// <c>BasketCheckoutEvent.TotalAmount</c> as
    /// <c>Subtotal - DiscountAmount</c>. Preserved alongside
    /// <see cref="EffectiveSubtotal"/> for backwards compatibility
    /// with pre-Phase-8 consumers (the cart UI, the ETag handler).
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// customer-visible subtotal after all applied discounts
    /// have been summed + clamped + rounded per
    /// <see cref="BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply"/>'s
    /// contract. Computed at upsert time; the cart UI prefers this
    /// over the legacy <c>Subtotal - DiscountAmount</c> derivation
    /// because the helper's floor-at-zero + banker's-rounding policy
    /// is locked here (single source of truth shared with the
    /// Ordering finalize path). The legacy
    /// <see cref="DiscountAmount"/> + <see cref="Total"/> derivation
    /// stays in place for the audit window — they preserve the
    /// pre-Phase-8 semantics.
    /// </summary>
    public decimal EffectiveSubtotal { get; set; }

    public decimal Subtotal => Items.Sum(x => x.TotalPrice);

    /// <summary>Derived — the user-visible cart total. Not stored.</summary>
    public decimal Total => Math.Max(EffectiveSubtotal > 0 ? EffectiveSubtotal : Subtotal - DiscountAmount, 0m);

    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }

    /// <summary>
    /// Last write timestamp. Updated on every StoreBasket and on the
    /// admin <c>PUT /api/v1/admin/carts/{userId}</c> path
    /// (the admin mutation runs the same upsert as the user-facing
    /// one). Drives the <c>Last-Modified</c> response header on
    /// <c>GET /api/v1/cart</c> so clients can issue
    /// <c>If-Modified-Since</c> conditional requests — the handler
    /// returns <c>304 Not Modified</c> when the supplied header is
    /// &gt;= this value, saving the response-body round-trip.
    /// </summary>
    public Instant LastModifiedAt { get; set; }
}

/// <summary>
/// Per-coupon breakdown populated by <c>StoreBasketHandler</c>. The
/// fields mirror the wire shape of <c>Discount.Grpc.CouponModel</c>
/// as resolved at upsert time, plus the basket-local
/// <see cref="DiscountAmount"/> (the coupon's contribution to the
/// cart total) and <see cref="AppliedAt"/>.
/// </summary>
/// <remarks>
/// Replaces the wire-shape <c>List&lt;string&gt;</c> coupon
/// list for the v2 <c>BasketCheckoutEvent</c> payload
/// (BuildingBlocks contribution). For the
/// snapshot lives only on the <see cref="Basket"/> document;
/// <c>BasketCheckoutEvent</c> still carries <c>AppliedDiscounts</c>
/// as strings.
/// </remarks>
public record CouponSnapshot(
    string Code,
    string Description,
    decimal DiscountAmount,
    Instant AppliedAt);
