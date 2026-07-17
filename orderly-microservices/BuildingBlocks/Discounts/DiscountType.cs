namespace BuildingBlocks.Discounts;

/// <summary>
/// Closed discriminator for the shape of a coupon's <c>Amount</c> field.
/// Stored as <c>int</c> in Discount's SQLite column (per the EF Core
/// <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>
/// at <c>CouponConfiguration.cs</c>); rendered as a string on the bus by the
/// project's <c>JsonStringEnumConverter</c> (per
/// <c>OrderActivityJson.Options</c>'s sister at <c>DiscountActivityJson.Options</c>).
/// </summary>
/// <remarks>
/// <para><b>Why this is in <c>BuildingBlocks.Discounts</c>, not <c>Discount.Grpc</c>:</b>
/// <c>Basket</c> reads this enum to compute its preview-time
/// <see cref="EffectiveSubtotal"/> without pulling in the Discount gRPC
/// service. Sharing the type lives in BuildingBlocks so both sides
/// compile against the same discriminator without an RPC roundtrip.</para>
/// <para><b>Why two separate enums:</b> per plan v1.4 Decision A,
/// <see cref="DiscountType"/> (Percentage, FixedAmount) and the
/// entity-side <c>RewardKind</c> (Percentage, FixedAmount, FreeItem,
/// Points) are intentionally distinct. A future consolidation would
/// need a v2 BuildingBlocks contribution.</para>
/// </remarks>
public enum DiscountType
{
    /// <summary>
    /// <c>Amount</c> is a percentage in <c>(0, 100]</c>. Validator enforces
    /// the range per plan §0.3.3.
    /// </summary>
    Percentage = 0,

    /// <summary>
    /// <c>Amount</c> is a fixed currency amount <c>&gt; 0</c>. Per
    /// <see cref="BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply"/>'s
    /// floor-at-zero clamp, a FixedAmount discount cannot drive the
    /// effective subtotal below 0.
    /// </summary>
    FixedAmount = 1,
}
