using System.Collections.Concurrent;

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
/// Upserts the cart and, on the way through, resolves the user-input
/// <see cref="Models.Basket.AppliedDiscounts"/> against Discount.Grpc
/// (per-coupon polyfill via <see cref="IDiscountLookup"/>) so the cart
/// carries a server-side <see cref="Models.Basket.DiscountAmount"/>
/// snapshot alongside the per-coupon <see cref="Models.CouponSnapshot"/>
/// breakdown.
/// </summary>
/// <remarks>
/// <para>
/// The aggregated <c>EvaluateDiscounts</c>
/// RPC lives on Discount's roadmap (not this plan). Until it ships,
/// the handler iterates the user-input coupon list in parallel
/// (<see cref="Parallel.ForEachAsync{T}"/>, <c>MaxDegreeOfParallelism = 4</c>),
/// mirrors <c>Discount.Grpc.Domain.ActiveNow.Coupon</c>'s eligibility
/// gate (minus the <c>DeletedAt</c> half — Discount's global query
/// filter excludes soft-deleted coupons before they reach the wire),
/// and sums the per-coupon contributions into
/// <see cref="Models.Basket.DiscountAmount"/>, clamped to
/// <see cref="Models.Basket.Subtotal"/>.
/// </para>
/// <para>
/// <b>gRPC failure policy = fail-closed.</b> If <see cref="IDiscountLookup.GetCouponAsync"/>
/// throws (broker down, auth failure, transient network error, malformed
/// wire <c>ExpirationDate</c>), the whole upsert fails — the alternative
/// (skip-and-log) lets a broken Discount integration silently bypass
/// coupon validation on a money path. A future Idempotency-Key
/// middleware lets the caller safely retry.
/// </para>
/// <para>
/// <b>Per-coupon clamping.</b> Each <see cref="Models.CouponSnapshot.DiscountAmount"/>
/// on <see cref="Models.Basket.AppliedCoupons"/> is the coupon's
/// contribution unclamped to the cart subtotal; the basket-level
/// <see cref="Models.Basket.DiscountAmount"/> is the clamp
/// (<c>Min(sum, subtotal)</c>). Predictable, easy to test, no cascading
/// logic. Cascade-clamping is a v2 concern if/when the UI displays
/// per-coupon contributions to the customer.
/// </para>
/// </remarks>
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
            basket.DiscountAmount = 0m;
            basket.LastModifiedAt = SystemClock.Instance.GetCurrentInstant();

            var (storedEmpty, isCreatedEmpty) = await basketRepository.StoreBasketAsync(basket, cancellationToken);
            return new StoreBasketResult(isCreatedEmpty, storedEmpty.UserId, storedEmpty.RestaurantId);
        }

        var subtotal = basket.Subtotal;
        var now = SystemClock.Instance.GetCurrentInstant();
        var snapshots = new ConcurrentBag<Models.CouponSnapshot>();

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

                // Eligibility — mirrors Discount.Grpc.Domain.ActiveNow.Coupon
                // (the source-side global query filter excludes soft-deleted
                // coupons before they reach the wire, so the DeletedAt half
                // is enforced at Discount and doesn't need to be re-checked
                // here).
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

                // Per-coupon contribution. The wire `Amount` was already
                // widened from double to decimal by the lookup; no further
                // rounding needed beyond what the percentage branch applies.
                var perCoupon = snapshot.DiscountType switch
                {
                    DiscountType.CouponPercentage => Math.Round(subtotal * snapshot.Amount / 100m, 2, MidpointRounding.ToEven),
                    DiscountType.CouponFixedAmount => snapshot.Amount,
                    // UNSPECIFIED = pre-Phase-8 legacy row; treat as no
                    // discount rather than guessing the semantic of `Amount`.
                    _ => 0m,
                };

                snapshots.Add(new Models.CouponSnapshot(
                    Code: couponCode,
                    Description: snapshot.Description,
                    DiscountAmount: perCoupon,
                    AppliedAt: now));
            });

        basket.AppliedCoupons = snapshots.ToList();
        // Final clamp: cumulative discounts can't push the cart total
        // below zero. Each snapshot's DiscountAmount stays unclamped
        // (per-coupon "would-be" contribution) — only the basket-level
        // total is clamped.
        basket.DiscountAmount = Math.Min(snapshots.Sum(s => s.DiscountAmount), subtotal);
        // Stamp LastModifiedAt AFTER the snapshot loop — every PUT
        // bumps it; GET /cart uses it for the Last-Modified response
        // header. Co-locating the stamp here keeps the
        // ETag / Last-Modified handshake deterministic (a single
        // timestamp per upsert, no race between snapshot creation
        // and persistence).
        basket.LastModifiedAt = now;

        var (stored, isCreated) = await basketRepository.StoreBasketAsync(basket, cancellationToken);
        return new StoreBasketResult(isCreated, stored.UserId, stored.RestaurantId);
    }
}
