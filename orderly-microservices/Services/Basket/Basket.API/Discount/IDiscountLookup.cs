namespace Basket.API.Discount;

/// <summary>
/// Basket-side abstraction over the Discount.Grpc <c>GetDiscount</c>
/// RPC. Decouples the cart handlers from gRPC's
/// <c>AsyncUnaryCall&lt;T&gt;</c> ceremony so the discount loop is
/// unit-testable with a plain NSubstitute mock.
/// </summary>
/// <remarks>
/// Phase 2 v1 polyfill: one call per coupon code. The aggregated
/// <c>EvaluateDiscounts</c> RPC lives on Discount's roadmap (not this
/// plan); once it ships, <see cref="IDiscountLookup"/> gains a batch
/// overload and the polyfill in <c>StoreBasketHandler</c> collapses to
/// a single call. The interface contract — one <see cref="DiscountSnapshot"/>
/// per code — stays stable.
/// </remarks>
public interface IDiscountLookup
{
    /// <summary>
    /// Resolves a single coupon code to a <see cref="DiscountSnapshot"/>.
    /// Implementations translate the wire shape (gRPC string date,
    /// double amount, closed enum) into the basket-side decimal + NodaTime
    /// shape and fail-closed on parse errors.
    /// </summary>
    /// <param name="restaurantId">The tenant id (from the JWT).</param>
    /// <param name="code">The user-input coupon code.</param>
    /// <param name="cancellationToken">Propagated to the underlying RPC.</param>
    /// <returns>
    /// A <see cref="DiscountSnapshot"/> with all wire fields normalised.
    /// Discount.Grpc returns an empty <c>IsActive=false</c> snapshot when
    /// the code does not match a row — that shape is preserved; the
    /// handler treats <c>IsActive=false</c> as the "skip" signal.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the wire <c>ExpirationDate</c> cannot be parsed
    /// (fail-closed on a money path).
    /// </exception>
    Task<DiscountSnapshot> GetCouponAsync(Guid restaurantId, string code, CancellationToken cancellationToken);
}

/// <summary>
/// Basket-side, wire-normalised view of a Discount.Grpc
/// <c>CouponModel</c>. Fields match the protobuf one-for-one except
/// where the wire format is lossy: <c>Amount</c> (wire <c>double</c>) is
/// widened to <see cref="decimal"/> here, and <c>ExpirationDate</c>
/// (wire <c>string</c>) is parsed to <see cref="Instant"/>.
/// </summary>
/// <param name="Code">The coupon code as Discount returned it (may be
/// empty when the code did not match a row).</param>
/// <param name="Description">Operator-supplied description; empty when
/// the code did not match a row.</param>
/// <param name="Amount">
/// <see cref="DiscountType.CouponPercentage"/>: percentage points (0–100).
/// <see cref="DiscountType.CouponFixedAmount"/>: flat currency value.
/// <see cref="DiscountType.CouponDiscountTypeUnspecified"/>: legacy value, the handler
/// treats it as zero regardless of <see cref="Amount"/>.
/// </param>
/// <param name="DiscountType">Closed discriminator governing the
/// semantic of <see cref="Amount"/>.</param>
/// <param name="IsActive">
/// <see langword="false"/> when the code did not match a row (Discount's
/// "not found" sentinel) OR when an admin disabled the coupon. The
/// handler treats both as the "skip" signal.
/// </param>
/// <param name="ExpirationDate">
/// <see langword="null"/> when the coupon has no expiry. Unparseable
/// wire values cause <see cref="IDiscountLookup.GetCouponAsync"/> to
/// throw before this record is constructed.
/// </param>
public record DiscountSnapshot(
    string Code,
    string Description,
    decimal Amount,
    DiscountType DiscountType,
    bool IsActive,
    Instant? ExpirationDate);
