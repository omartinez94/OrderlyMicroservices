using NodaTime.Text;

namespace Basket.API.Discount;

/// <summary>
/// gRPC implementation of <see cref="IDiscountLookup"/>. Wraps the
/// generated <c>DiscountProtoService.DiscountProtoServiceClient</c>
/// and normalises the wire shape into the basket-side
/// <see cref="DiscountSnapshot"/> (decimal <c>Amount</c>, NodaTime
/// <c>Instant</c> <c>ExpirationDate</c>, closed enum).
/// </summary>
/// <remarks>
/// Fail-closed policy: a malformed wire <c>ExpirationDate</c> throws
/// <see cref="InvalidOperationException"/> rather than silently
/// accepting the coupon. The handler depends on the snapshot's
/// <see cref="Instant"/> value to be parseable — a defaulted
/// <c>Instant.MinValue</c> would silently pass the
/// <c>ExpirationDate &lt; now</c> check on every call.
/// </remarks>
internal sealed class GrpcDiscountLookup(
    DiscountProtoService.DiscountProtoServiceClient client,
    ILogger<GrpcDiscountLookup> logger)
    : IDiscountLookup
{
    public async Task<DiscountSnapshot> GetCouponAsync(Guid restaurantId, string code, CancellationToken cancellationToken)
    {
        var response = await client.GetDiscountAsync(
            new GetDiscountRequest
            {
                RestaurantId = restaurantId.ToString(),
                Code = code,
            },
            cancellationToken: cancellationToken);

        var coupon = response.Coupon;

        Instant? expiration = null;
        if (!string.IsNullOrEmpty(coupon.ExpirationDate))
        {
            var parse = InstantPattern.ExtendedIso.Parse(coupon.ExpirationDate);
            if (!parse.Success)
            {
                logger.LogError(
                    "Discount.Grpc returned a malformed ExpirationDate '{WireValue}' for coupon {CouponCode} (restaurant {RestaurantId}). Failing closed.",
                    coupon.ExpirationDate, code, restaurantId);
                throw new InvalidOperationException(
                    $"Discount.Grpc returned a malformed ExpirationDate '{coupon.ExpirationDate}' for coupon {code}.");
            }
            expiration = parse.Value;
        }

        return new DiscountSnapshot(
            Code: coupon.Code,
            Description: coupon.Description,
            // Wire `Amount` is double (proto3 limitation); round-trip to
            // decimal to avoid cascading rounding error in the basket's
            // running DiscountAmount total.
            Amount: (decimal)coupon.Amount,
            DiscountType: coupon.DiscountType,
            IsActive: coupon.IsActive,
            ExpirationDate: expiration);
    }
}
