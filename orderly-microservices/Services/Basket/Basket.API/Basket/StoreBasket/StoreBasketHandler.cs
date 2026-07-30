using System.Collections.Concurrent;
using BuildingBlocks.Discounts;

namespace Basket.API.Basket.StoreBasket;

public record StoreBasketCommand(Models::Basket Basket) : ICommand<StoreBasketResult>, IBasketIdentityRequest
{
    public Guid UserId => Basket.UserId;
    public Guid RestaurantId => Basket.RestaurantId;
}

public record StoreBasketResult(bool IsCreated, Guid UserId, Guid RestaurantId);

public class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Basket).NotNull().WithMessage("Basket is required.");

        // Validation rules. The endpoint overwrites
        // `Basket.UserId` / `Basket.RestaurantId` from the JWT before
        // constructing the command, so the body-shape spoofing
        // check (`Equal(Guid.Empty)` on the body's identity fields)
        // cannot run from this layer — the values are the JWT
        // values, not `Guid.Empty`, by the time this validator
        // runs. The endpoint overwrite + the second-layer
        // `BasketIdentityGuardBehavior` cross-check are the
        // spoofing protection.
        RuleFor(x => x.Basket.Items).NotNull().WithMessage("Basket.Items is required.");
        RuleFor(x => x.Basket.Items.Count).LessThanOrEqualTo(100)
            .WithMessage("Basket.Items.Count must be <= 100 (per §0.4.10).");
        RuleForEach(x => x.Basket.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.MenuItemId).GreaterThan(0)
                .WithMessage("BasketItem.MenuItemId must be > 0.");
            item.RuleFor(x => x.Quantity).InclusiveBetween(1, 99)
                .WithMessage("BasketItem.Quantity must be in [1, 99] (per §0.4.10).");
            item.RuleFor(x => x.UnitPrice).GreaterThan(0)
                .WithMessage("BasketItem.UnitPrice must be > 0.");
            item.RuleFor(x => x.Variations.Count).LessThanOrEqualTo(10)
                .WithMessage("BasketItem.Variations.Count must be <= 10 (per §0.4.10).");
            item.RuleForEach(x => x.Variations).ChildRules(v =>
            {
                v.RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
                v.RuleFor(x => x.Value).NotEmpty().MaximumLength(64);
                v.RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
            });
            item.RuleFor(x => x.Customizations.Count).LessThanOrEqualTo(20)
                .WithMessage("BasketItem.Customizations.Count must be <= 20 (per §0.4.10).");
        });

        // Distinct coupon codes, count <= 10, each code
        // matches ^[A-Z0-9_-]{4,32}$.
        RuleFor(x => x.Basket.AppliedDiscounts).NotNull();
        RuleFor(x => x.Basket.AppliedDiscounts.Count).LessThanOrEqualTo(10)
            .WithMessage("Basket.AppliedDiscounts.Count must be <= 10 (per §0.4.10).");
        RuleFor(x => x.Basket.AppliedDiscounts)
            .Must(codes => codes == null || codes.Distinct().Count() == codes.Count)
            .WithMessage("Basket.AppliedDiscounts must be distinct (per §0.4.10).");
        RuleForEach(x => x.Basket.AppliedDiscounts)
            .Matches("^[A-Z0-9_-]{4,32}$")
            .WithMessage("Basket.AppliedDiscounts[i] must match ^[A-Z0-9_-]{4,32}$ (per §0.4.10).");
    }
}

/// <summary>
/// Phase 8 upsert handler — uses <see cref="ApplyDiscountsHelper"/>
/// from BuildingBlocks.Discounts as the single source of truth for
/// stacking math. The helper is shared with Ordering's finalize-time
/// deduction path so a basket preview and a finalized-order deduction
/// compute the same numbers from the same input.
/// </summary>
public class StoreBasketHandler(
    IBasketRepository basketRepository,
    IDiscountLookup discountLookup,
    ILogger<StoreBasketHandler> logger)
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    private const int DiscountLookupParallelism = 4;

    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = command.Basket;

        // Empty-coupon and empty-cart short-circuits: no Discount lookup
        // is needed, no work to do. Skipping the parallel loop also
        // keeps the audit trail clean (no `Coupon X skipped (inactive)`
        // debug lines on the no-discount path).
        if (basket.AppliedDiscounts.Count == 0 || basket.Items.Count == 0)
        {
            basket.AppliedCoupons = [];
            basket.AppliedDiscountBreakdown = [];
            basket.DiscountAmount = 0m;
            basket.EffectiveSubtotal = basket.Subtotal;
            basket.LastModifiedAt = SystemClock.Instance.GetCurrentInstant();

            var (storedEmpty, isCreatedEmpty) = await basketRepository.StoreBasketAsync(basket, cancellationToken);
            return new StoreBasketResult(isCreatedEmpty, storedEmpty.UserId, storedEmpty.RestaurantId);
        }

        var subtotal = basket.Subtotal;
        var now = SystemClock.Instance.GetCurrentInstant();
        var snapshots = new ConcurrentBag<Models.CouponSnapshot>();
        var applied = new ConcurrentBag<AppliedDiscount>();

        // Per-coupon resolution — same shape as before, but the
        // per-coupon output now feeds `AppliedDiscountsHelper.Apply(...)`
        // instead of an inline switch + Math.Round. Fail-closed on gRPC
        // errors (a broker outage, auth failure, or transient network
        // error) — the whole upsert fails rather than silently
        // bypassing coupon validation on a money path.
        await Parallel.ForEachAsync(
            basket.AppliedDiscounts,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DiscountLookupParallelism,
                CancellationToken = cancellationToken,
            },
            async (couponCode, innerCt) =>
            {
                var snapshot = await discountLookup.GetCouponAsync(
                    basket.RestaurantId, couponCode, innerCt);

                if (!snapshot.IsActive)
                {
                    logger.LogDebug(
                        "Coupon {CouponCode} skipped — IsActive=false (restaurant {RestaurantId}).",
                        couponCode, basket.RestaurantId);
                    return;
                }

                if (snapshot.ExpirationDate is { } expires && expires < now)
                {
                    logger.LogDebug(
                        "Coupon {CouponCode} skipped — expired at {ExpirationDate}.",
                        couponCode, expires);
                    return;
                }

                snapshots.Add(new Models.CouponSnapshot(
                    Code: couponCode,
                    Description: snapshot.Description,
                    DiscountAmount: 0m, // populated below from the helper result
                    AppliedAt: now));

                // Translate proto-side DiscountType to the BuildingBlocks
                // counterpart. The proto enum's wire values are
                // COUPON_PERCENTAGE=1 / COUPON_FIXED_AMOUNT=2; the
                // BuildingBlocks enum's wire values are Percentage=0 /
                // FixedAmount=1. A direct int cast would mis-classify
                // every percentage as a fixed amount.
                var helperType = snapshot.DiscountType switch
                {
                    global::Discount.Grpc.DiscountType.CouponPercentage => BuildingBlocks.Discounts.DiscountType.Percentage,
                    global::Discount.Grpc.DiscountType.CouponFixedAmount => BuildingBlocks.Discounts.DiscountType.FixedAmount,
                    _ => BuildingBlocks.Discounts.DiscountType.Percentage, // UNSPECIFIED = treat as percentage; helper produces zero
                };

                // The IDiscountLookup snapshot doesn't carry CouponId
                // (only Code/Description/Amount/DiscountType/IsActive/
                // ExpirationDate per the wire shape); the helper's
                // AppliedDiscount requires it. We synthesize
                // CouponId = 0 here — the breakdown's CouponId is
                // informational for admins; the actual redemption at
                // Ordering finalize-time looks the row up by Code +
                // RestaurantId.
                applied.Add(new AppliedDiscount(
                    Type: helperType,
                    Amount: snapshot.Amount,
                    CouponId: 0,
                    Code: couponCode,
                    IsActive: true));
            });

        // Single source of truth for stacking math. Returns the
        // post-clamp effective subtotal + the per-row breakdown that
        // gets persisted as `BasketAppliedDiscount` rows.
        var result = ApplyDiscountsHelper.Apply(subtotal, [.. applied]);

        basket.AppliedCoupons = snapshots.ToList();
        // Legacy fields — preserved for the audit window so pre-Phase-8
        // code paths still see the same numbers. `DiscountAmount` is
        // the total reduction (clamped to subtotal); `EffectiveSubtotal`
        // is the helper's post-clamp result (== subtotal when no
        // coupons applied, or the running total after each coupon).
        basket.DiscountAmount = Math.Min(result.TotalReduction, subtotal);
        basket.EffectiveSubtotal = result.EffectiveSubtotal;
        basket.AppliedDiscountBreakdown = result.Breakdown
            .Select(b => new Models.BasketAppliedDiscount(
                CouponId: b.CouponId,
                Code: b.Code,
                DiscountType: (int)b.Type,
                RequestedAmount: b.RequestedAmount,
                AppliedAmount: b.AppliedAmount,
                AppliedAt: now))
            .ToList();

        // Stamp LastModifiedAt AFTER the helper call — every PUT bumps
        // it; GET /cart uses it for the Last-Modified response header.
        // Co-locating the stamp here keeps the ETag / Last-Modified
        // handshake deterministic (a single timestamp per upsert, no
        // race between snapshot creation and persistence).
        basket.LastModifiedAt = now;

        logger.LogInformation(
            "StoreBasket applied {Count} coupons; subtotal={Subtotal} effective={Effective} reduction={Reduction}",
            result.Breakdown.Count, result.OriginalSubtotal, result.EffectiveSubtotal, result.TotalReduction);

        var (stored, isCreated) = await basketRepository.StoreBasketAsync(basket, cancellationToken);
        return new StoreBasketResult(isCreated, stored.UserId, stored.RestaurantId);
    }
}