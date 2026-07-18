using Basket.API.Basket.StoreBasket;
using Discount.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="StoreBasketHandler"/>'s Phase 2.2
/// real-discount integration. Locks the eligibility gate
/// (<c>IsActive &amp;&amp; (ExpirationDate empty || &gt;= now)</c>) and
/// the per-coupon / basket-level clamp semantics (per-coupon snapshot
/// is unclamped; basket <c>DiscountAmount</c> is clamped to
/// <c>Subtotal</c>).
/// </summary>
/// <remarks>
/// Mirrors <c>Discount.Grpc.Domain.ActiveNow.Coupon</c>'s eligibility
/// gate minus the <c>DeletedAt</c> half (Discount's global query filter
/// excludes soft-deleted coupons before they reach the wire — verified
/// in <c>DiscountService.GetDiscount</c> at <c>Discount.Grpc/Services/DiscountService.cs:27</c>).
/// Mocks the basket-side <see cref="IDiscountLookup"/> abstraction
/// (the gRPC client is wrapped by <c>GrpcDiscountLookup</c> in
/// <c>Basket.API/Discount/</c>) so each test arranges a
/// <see cref="DiscountSnapshot"/> directly.
/// </remarks>
public sealed class StoreBasketHandlerTests
{
    [Fact]
    public async Task EmptyAppliedDiscounts_NoDiscountApplied_AndCartStored()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var basket = new Models.Basket(userId, restaurantId)
        {
            Items = { new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 10m } },
            // AppliedDiscounts intentionally empty.
        };

        var repository = Substitute.For<IBasketRepository>();
        repository
            .StoreBasketAsync(Arg.Any<Models.Basket>(), Arg.Any<CancellationToken>())
            .Returns(basket);

        var discountLookup = Substitute.For<IDiscountLookup>();
        var handler = new StoreBasketHandler(
            repository,
            discountLookup,
            NullLogger<StoreBasketHandler>.Instance);

        var result = await handler.Handle(
            new StoreBasketCommand(basket),
            CancellationToken.None);

        result.UserId.Should().Be(userId);
        result.RestaurantId.Should().Be(restaurantId);
        basket.DiscountAmount.Should().Be(0m);
        basket.AppliedCoupons.Should().BeEmpty();

        // No coupons → no Discount lookups.
        await discountLookup
            .DidNotReceive()
            .GetCouponAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await repository
            .Received(1)
            .StoreBasketAsync(Arg.Is<Models.Basket>(b => b.DiscountAmount == 0m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidPercentageCoupon_DiscountAmountComputed_AsPercentageOfSubtotal()
    {
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["TENOFF"]);

        var (handler, _, discountLookup) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["TENOFF"] = new(
                    Code: "TENOFF",
                    Description: "10% off",
                    Amount: 10m,
                    DiscountType: DiscountType.CouponPercentage,
                    IsActive: true,
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(10m); // 10% of 100
        basket.AppliedCoupons.Should().HaveCount(1);
        basket.AppliedCoupons[0].Code.Should().Be("TENOFF");
        basket.AppliedCoupons[0].DiscountAmount.Should().Be(10m);
        basket.Total.Should().Be(90m);
    }

    [Fact]
    public async Task ValidFixedAmountCoupon_DiscountAmountComputed_AsFlatValue()
    {
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["FIVEOFF"]);

        var (handler, _, discountLookup) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["FIVEOFF"] = new(
                    Code: "FIVEOFF",
                    Description: "$5 off",
                    Amount: 5m,
                    DiscountType: DiscountType.CouponFixedAmount,
                    IsActive: true,
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(5m);
        basket.Total.Should().Be(95m);
    }

    [Fact]
    public async Task InactiveCoupon_Skipped()
    {
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["INACTIVE"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["INACTIVE"] = new(
                    Code: "INACTIVE",
                    Description: string.Empty,
                    Amount: 50m,
                    DiscountType: DiscountType.CouponFixedAmount,
                    IsActive: false, // <-- skip signal
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(0m);
        basket.AppliedCoupons.Should().BeEmpty();
        basket.Total.Should().Be(basket.Subtotal);
    }

    [Fact]
    public async Task ExpiredCoupon_Skipped()
    {
        // An instant 1 hour in the past — guaranteed to be expired
        // regardless of test-runner clock drift.
        var pastInstant = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromHours(1));

        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["OLDPROMO"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["OLDPROMO"] = new(
                    Code: "OLDPROMO",
                    Description: string.Empty,
                    Amount: 20m,
                    DiscountType: DiscountType.CouponFixedAmount,
                    IsActive: true,
                    ExpirationDate: pastInstant),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(0m);
        basket.AppliedCoupons.Should().BeEmpty();
    }

    [Fact]
    public async Task CouponNotFound_TreatedAsInactive_AndSkipped()
    {
        // Discount returns an empty DiscountSnapshot with IsActive=false when
        // the code doesn't match a row (DiscountService.GetDiscount line 30-44).
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["NOSUCHCODE"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["NOSUCHCODE"] = new(
                    Code: string.Empty,
                    Description: string.Empty,
                    Amount: 0m,
                    DiscountType: DiscountType.CouponDiscountTypeUnspecified,
                    IsActive: false,
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(0m);
        basket.AppliedCoupons.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleCoupons_SumOfDiscounts()
    {
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["TENPCT", "FIVEFLAT"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["TENPCT"] = new("TENPCT", string.Empty, 10m, DiscountType.CouponPercentage, true, null),
                ["FIVEFLAT"] = new("FIVEFLAT", string.Empty, 5m, DiscountType.CouponFixedAmount, true, null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(15m); // 10 + 5
        basket.AppliedCoupons.Should().HaveCount(2);
        basket.Total.Should().Be(85m);
    }

    [Fact]
    public async Task DiscountExceedsSubtotal_ClampedToSubtotal()
    {
        var basket = BuildBasket(subtotal: 8m, appliedDiscounts: ["TOOMUCH"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["TOOMUCH"] = new(
                    Code: "TOOMUCH",
                    Description: string.Empty,
                    Amount: 50m, // would-be discount: $50 off an $8 cart
                    DiscountType: DiscountType.CouponFixedAmount,
                    IsActive: true,
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        // Per-coupon snapshot is unclamped: the would-be $50 contribution
        // is recorded on the snapshot.
        basket.AppliedCoupons.Should().HaveCount(1);
        basket.AppliedCoupons[0].DiscountAmount.Should().Be(50m);

        // Basket-level DiscountAmount is clamped to subtotal.
        basket.DiscountAmount.Should().Be(8m);
        basket.Total.Should().Be(0m);
    }

    [Fact]
    public async Task DiscountLookup_Failure_BubblesUp()
    {
        // Fail-closed on lookup errors (per the handler's gRPC failure
        // policy comment). The alternative — skip-and-log — would let a
        // broken Discount integration silently bypass coupon validation
        // on a money path.
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["ANYCODE"]);

        var repository = Substitute.For<IBasketRepository>();
        repository.StoreBasketAsync(Arg.Any<Models.Basket>(), Arg.Any<CancellationToken>())
            .Returns(basket);

        var discountLookup = Substitute.For<IDiscountLookup>();
        discountLookup
            .GetCouponAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DiscountSnapshot>>(_ => throw new InvalidOperationException("broker down"));

        var handler = new StoreBasketHandler(
            repository,
            discountLookup,
            NullLogger<StoreBasketHandler>.Instance);

        var act = () => handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("broker down");
    }

    [Fact]
    public async Task UnspecifiedDiscountType_TreatedAsZero()
    {
        // Pre-Phase-8 legacy rows may carry UNSPECIFIED on the wire.
        // Apply 0 rather than guessing the Amount semantic.
        var basket = BuildBasket(subtotal: 100m, appliedDiscounts: ["LEGACY"]);

        var (handler, _, _) = BuildHandler(
            basket,
            snapshots: new Dictionary<string, DiscountSnapshot>
            {
                ["LEGACY"] = new(
                    Code: "LEGACY",
                    Description: string.Empty,
                    Amount: 50m, // would-be amount — ignored because UNSPECIFIED
                    DiscountType: DiscountType.CouponDiscountTypeUnspecified,
                    IsActive: true,
                    ExpirationDate: null),
            });

        await handler.Handle(new StoreBasketCommand(basket), CancellationToken.None);

        basket.DiscountAmount.Should().Be(0m);
        basket.AppliedCoupons.Should().HaveCount(1);
        basket.AppliedCoupons[0].DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public void BasketTotal_Derived_AsSubtotalMinusDiscount()
    {
        // Pure derived-property test — locks the §0.4.7 GET /cart projection
        // shape without running the handler.
        var basket = new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items = { new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 50m } },
            DiscountAmount = 12.34m,
        };

        basket.Subtotal.Should().Be(50m);
        basket.Total.Should().Be(37.66m);

        // Defensive: clamp to 0 if discount exceeds subtotal.
        basket.DiscountAmount = 999m;
        basket.Total.Should().Be(0m);
    }

    // ----------------------------------------------------------------------
    // Test helpers
    // ----------------------------------------------------------------------

    private static Models.Basket BuildBasket(decimal subtotal, IReadOnlyList<string> appliedDiscounts)
    {
        // Split the target subtotal across N items at $1 each so the
        // math stays simple; the last item absorbs the rounding delta.
        var items = new List<Models.BasketItem>();
        for (var i = 0; i < (int)subtotal; i++)
        {
            items.Add(new Models.BasketItem { MenuItemId = i + 1, Quantity = 1, UnitPrice = 1m });
        }
        var remainder = subtotal - (int)subtotal;
        if (remainder > 0)
        {
            items.Add(new Models.BasketItem { MenuItemId = items.Count + 1, Quantity = 1, UnitPrice = remainder });
        }

        var basket = new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items = { },
        };
        foreach (var item in items)
        {
            basket.Items.Add(item);
        }
        foreach (var code in appliedDiscounts)
        {
            basket.AppliedDiscounts.Add(code);
        }
        return basket;
    }

    private static (
        StoreBasketHandler Handler,
        IBasketRepository Repository,
        IDiscountLookup DiscountLookup)
        BuildHandler(Models.Basket basket, IReadOnlyDictionary<string, DiscountSnapshot> snapshots)
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.StoreBasketAsync(Arg.Any<Models.Basket>(), Arg.Any<CancellationToken>())
            .Returns(basket);

        var discountLookup = Substitute.For<IDiscountLookup>();
        foreach (var (code, snapshot) in snapshots)
        {
            discountLookup
                .GetCouponAsync(basket.RestaurantId, code, Arg.Any<CancellationToken>())
                .Returns(snapshot);
        }

        var handler = new StoreBasketHandler(
            repository,
            discountLookup,
            NullLogger<StoreBasketHandler>.Instance);

        return (handler, repository, discountLookup);
    }
}
