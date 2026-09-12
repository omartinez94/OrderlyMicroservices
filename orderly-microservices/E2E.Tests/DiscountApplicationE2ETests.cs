using Discount.Grpc;
using E2E.Tests.Abstractions;
using Grpc.Core;
using Polly;
using Polly.Retry;

namespace E2E.Tests;

/// <summary>
/// Discount-application E2E tests. Exercises the
/// Discount.Grpc → Basket → Ordering chain plus the
/// Catalog → Discount reward-code feedback path. Each test assumes
/// the full stack is up (see <see cref="E2ETestEnvironment"/>).
/// </summary>
[Trait("Category", "E2E")]
[Collection(nameof(E2ETestEnvironmentCollection))]
public sealed class DiscountApplicationE2ETests(E2ETestEnvironment env)
{
    private readonly E2ETestEnvironment _env = env;

    /// <summary>
    /// Create a coupon via Discount.Grpc → add items + coupon code to
    /// the cart → checkout → assert the order's discount fields reflect
    /// the coupon. This is the Discount → Basket → Ordering bridge.
    /// </summary>
    [Fact]
    public async Task DiscountAppliedToCart_ReflectedInOrder()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();

        // Permissions: discount-rule:edit for the gRPC create; the
        // usual basket + ordering suite for the rest of the flow.
        string token = _env.JwtMinter.Mint(
            userId, restaurantId,
            "discount-rule:edit", "discount-coupon:edit",
            "orders:create", "orders:write", "orders:view_own");

        string couponCode = $"E2E-{Guid.NewGuid():N}".Substring(0, 12);

        // 1. Create a coupon via gRPC — use a real gRPC channel rather
        //    than HTTP because that's the production surface the
        //    Basket checkout handler calls.
        using var channel = GrpcChannel.ForAddress(ServiceEndpoints.Discount);
        var couponClient = new DiscountProtoService.DiscountProtoServiceClient(channel);

        // Discount.Grpc auth wiring: when DevJwtBearerFallback is
        // registered, gRPC metadata `authorization: Bearer ...` is
        // honored. The factory below attaches the same mint.
        var coupon = new CouponModel
        {
            RestaurantId = restaurantId.ToString(),
            Code = couponCode,
            Description = "E2E test coupon",
            Amount = 2.50,
            DiscountType = DiscountType.CouponFixedAmount,
            IsActive = true,
        };

        CreateDiscountResponse created = await couponClient.CreateDiscountAsync(
            new CreateDiscountRequest { Coupon = coupon },
            new Metadata { { "authorization", $"Bearer {token}" } });
        created.Success.Should().BeTrue();

        // 2. Add items + the coupon code to the cart. Basket's
        //    ApplyDiscountsHelper runs at store-time and stores the
        //    discount breakdown in Marten-embedded AppliedDiscountBreakdown.
        var cart = new
        {
            userId = Guid.Empty,
            restaurantId = Guid.Empty,
            items = new[]
            {
                new { menuItemId = Guid.NewGuid(), menuItemName = "Salad", quantity = 1, price = 10.00m },
            },
            couponCode,
        };
        using (HttpResponseMessage put = await _env.Basket.SendAsync(
            HttpMethod.Put, "/v1/cart", cart, token))
        {
            put.EnsureSuccessStatusCode();
        }

        // 3. Checkout → wait for the Order to materialize → assert the
        //    persisted bill carries the discount.
        using (HttpResponseMessage post = await _env.Basket.SendAsync(
            HttpMethod.Post, "/v1/cart/checkout",
            new { userId = Guid.Empty, restaurantId = Guid.Empty, totalAmount = 10.00m, deliveryAddress = "Y" },
            token))
        {
            post.EnsureSuccessStatusCode();
        }

        Guid orderId = await PollForOrderCreated(userId, restaurantId, token);
        var order = await _env.Ordering.GetJsonAsync<OrderResponse>($"/v1/orders/{orderId}", token);
        order.Should().NotBeNull();
        // Discount is persisted in `discountAmount` / `discountCode` on the
        // OrderDto — Phase 3 wiring closed the zeroed-bill gap. The exact
        // assertion depends on the helper's behaviour; the lower bound is
        // "non-zero" because we know the helper stored something.
        order!.discountAmount.Should().BeGreaterThan(0m);
        order.discountCode.Should().Be(couponCode);
    }

    /// <summary>
    /// Catalog → Discount feedback reward: submit feedback via the
    /// Catalog API → the Discount consumer creates a RewardCode →
    /// apply that code on the next cart and verify the discount
    /// applies. Documents the cross-service feedback reward flow.
    /// </summary>
    [Fact]
    public async Task FeedbackRewardRedemption()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        string token = _env.JwtMinter.Mint(
            userId, restaurantId,
            "feedback:submit", "orders:create", "orders:view_own",
            "discount-rule:view");

        // 1. Submit feedback via Catalog. The exact endpoint shape is
        //    /v1/customer-feedback (POST). Plan §6.1 lists it among the
        //    CustomerFeedback endpoint tests.
        var feedback = new
        {
            userId,
            restaurantId,
            rating = 5,
            comment = "Great service",
        };
        using (HttpResponseMessage post = await _env.Catalog.SendAsync(
            HttpMethod.Post, "/v1/customer-feedback", feedback, token))
        {
            post.EnsureSuccessStatusCode();
        }

        // 2. Poll Discount's RewardCode list for a code attributed to
        //    this user. Use the gRPC client.
        using var channel = GrpcChannel.ForAddress(ServiceEndpoints.Discount);
        var rewardClient = new RewardCodeProtoService.RewardCodeProtoServiceClient(channel);

        AsyncRetryPolicy<string?> poll = Policy<string?>
            .HandleResult(c => c is null)
            .WaitAndRetryAsync(
                retryCount: 30,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500));

        string? rewardCode = await poll.ExecuteAsync(async () =>
        {
            ListRewardCodesResponse list = await rewardClient.ListRewardCodesAsync(
                new ListRewardCodesRequest { RestaurantId = restaurantId.ToString() },
                new Metadata { { "authorization", $"Bearer {token}" } });
            return list.RewardCodes.FirstOrDefault()?.Code;
        });

        rewardCode.Should().NotBeNullOrEmpty("the feedback consumer should mint a reward code within ~15 s");

        // 3. Apply on the next cart + checkout. Reuse the same flow
        //    as DiscountAppliedToCart_ReflectedInOrder.
        var cart = new
        {
            userId = Guid.Empty,
            restaurantId = Guid.Empty,
            items = new[] { new { menuItemId = Guid.NewGuid(), menuItemName = "Combo", quantity = 1, price = 15.00m } },
            couponCode = rewardCode,
        };
        using (HttpResponseMessage put = await _env.Basket.SendAsync(
            HttpMethod.Put, "/v1/cart", cart, token))
        {
            put.EnsureSuccessStatusCode();
        }
        using (HttpResponseMessage checkout = await _env.Basket.SendAsync(
            HttpMethod.Post, "/v1/cart/checkout",
            new { userId = Guid.Empty, restaurantId = Guid.Empty, totalAmount = 15.00m, deliveryAddress = "Z" },
            token))
        {
            checkout.EnsureSuccessStatusCode();
        }

        Guid orderId = await PollForOrderCreated(userId, restaurantId, token);
        var order = await _env.Ordering.GetJsonAsync<OrderResponse>($"/v1/orders/{orderId}", token);
        order.Should().NotBeNull();
        order!.discountAmount.Should().BeGreaterThan(0m);
        order.discountCode.Should().Be(rewardCode);
    }

    // ----- helpers + DTOs -----

    private async Task<Guid> PollForOrderCreated(Guid userId, Guid restaurantId, string token)
    {
        AsyncRetryPolicy<Guid> poll = Policy<Guid>
            .HandleResult(id => id == Guid.Empty)
            .WaitAndRetryAsync(
                retryCount: 30,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500));

        return await poll.ExecuteAsync(async () =>
        {
            var orders = await _env.Ordering.GetJsonAsync<List<OrderResponse>>(
                $"/v1/orders/by-customer/{userId}", token);
            return orders?.FirstOrDefault()?.id ?? Guid.Empty;
        });
    }

    private sealed record OrderResponse(Guid id, string status, decimal discountAmount, string discountCode);
}
