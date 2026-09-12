using Grpc.Net.Client;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Covers <c>DiscountRuleProtoService.EvaluateDiscountRules</c> — the RPC the
/// Basket checkout path calls to find which coupons a cart qualifies for.
///
/// <para>Evaluation is deliberately permissive on malformed rule payloads: a
/// rule whose <c>RuleDataJson</c> does not parse is skipped rather than
/// failing the whole call, and an unparseable <c>restaurant_id</c> yields an
/// empty response instead of an <c>RpcException</c>. These tests pin that
/// contract.</para>
///
/// <para><b>Tenant isolation.</b> Phase 5 added the missing global query
/// filter on <c>DiscountRule</c> (see <c>DiscountContext.OnModelCreating</c>),
/// so evaluation is now scoped to the caller's tenant claim AND the request's
/// restaurant id. <see cref="EvaluateRules_CrossTenant_Isolated"/> asserts the
/// fixed behaviour: asking for another tenant's restaurant id returns
/// nothing, even when that tenant has a matching active rule.</para>
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class EvaluateDiscountRulesTests(DiscountWebApplicationFactory factory)
{
    /// <summary>The tenant baked into every test principal by
    /// <c>TestGrpcAuthInterceptor</c>.</summary>
    private const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";

    private static readonly Guid TestRestaurantGuid = new(TestRestaurantId);
    private static readonly Guid OtherRestaurantGuid = new("99999999-9999-9999-9999-999999999999");
    private static readonly Guid TestUserId = new("22222222-2222-2222-2222-222222222222");

    private static DiscountRuleProtoService.DiscountRuleProtoServiceClient BuildClient(
        DiscountWebApplicationFactory factory)
    {
        Uri address = factory.ClientOptions.BaseAddress
            ?? throw new InvalidOperationException("WebApplicationFactory.BaseAddress is null");
        HttpClient httpClient = factory.CreateClient();
        GrpcChannel channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient,
        });
        return new DiscountRuleProtoService.DiscountRuleProtoServiceClient(channel);
    }

    private static Metadata ReadMetadata() => new()
    {
        { "x-test-user", TestUserId.ToString() },
        { "x-test-permissions", DiscountPermissions.DiscountRuleRead },
    };

    /// <summary>
    /// Seeds a coupon plus one attached rule and returns the coupon id, which
    /// is what <c>EvaluateDiscountRules</c> echoes back as an applicable id.
    /// </summary>
    private async Task<int> SeedRuleAsync(
        Guid restaurantId,
        DiscountRuleKind kind,
        string ruleDataJson,
        bool isActive = true,
        string codePrefix = "EVAL")
    {
        Coupon coupon = await factory.SeedCouponAsync(
            restaurantId, code: $"{codePrefix}-{Guid.NewGuid():N}"[..16]);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DiscountContext db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        db.DiscountRules.Add(new DiscountRule
        {
            RestaurantId = restaurantId,
            CouponId = coupon.Id,
            RuleType = kind,
            RuleDataJson = ruleDataJson,
            IsActive = isActive,
        });
        await db.SaveChangesAsync();

        return coupon.Id;
    }

    [Fact]
    public async Task EvaluateRules_MatchingOrder_ReturnsApplicableDiscounts()
    {
        await factory.CleanAllAsync();
        int couponId = await SeedRuleAsync(
            TestRestaurantGuid,
            DiscountRuleKind.MinOrderAmount,
            """{"MinOrderAmount":"50.00"}""");

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        EvaluateDiscountRulesResponse response = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                OrderTotal = 75.00d,
            },
            ReadMetadata());

        response.ApplicableCouponIds.Should().Contain(couponId,
            "an order total of 75 clears the rule's 50 minimum");
    }

    [Fact]
    public async Task EvaluateRules_NoMatchingRules_ReturnsEmptyList()
    {
        await factory.CleanAllAsync();
        await SeedRuleAsync(
            TestRestaurantGuid,
            DiscountRuleKind.MinOrderAmount,
            """{"MinOrderAmount":"50.00"}""");

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        EvaluateDiscountRulesResponse response = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                OrderTotal = 10.00d,
            },
            ReadMetadata());

        response.ApplicableCouponIds.Should().BeEmpty(
            "an order total of 10 falls short of the rule's 50 minimum");
    }

    [Fact]
    public async Task EvaluateRules_InactiveRule_ExcludedFromResults()
    {
        // NOTE (plan deviation, §6.5): the plan names this
        // EvaluateRules_ExpiredRule_ExcludedFromResults. DiscountRule carries
        // no expiry column of its own — expiry lives on the parent Coupon, and
        // the rule-side switch the evaluator honours is IsActive (the
        // DiscountExpirySweepService flips it). This asserts the exclusion the
        // evaluator actually performs.
        await factory.CleanAllAsync();
        int couponId = await SeedRuleAsync(
            TestRestaurantGuid,
            DiscountRuleKind.MinOrderAmount,
            """{"MinOrderAmount":"50.00"}""",
            isActive: false);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        EvaluateDiscountRulesResponse response = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                OrderTotal = 75.00d,
            },
            ReadMetadata());

        response.ApplicableCouponIds.Should().NotContain(couponId,
            "the evaluator filters on r.IsActive — a deactivated rule never applies");
    }

    [Fact]
    public async Task EvaluateRules_CrossTenant_Isolated()
    {
        // Regression test for the Phase 5 tenant-filter fix. The caller's
        // principal is always tenant 1111…, so a rule seeded under 9999… must
        // stay invisible even when the request explicitly names that tenant.
        await factory.CleanAllAsync();
        int otherTenantCouponId = await SeedRuleAsync(
            OtherRestaurantGuid,
            DiscountRuleKind.MinOrderAmount,
            """{"MinOrderAmount":"10.00"}""",
            codePrefix: "XTEN");

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        // Ask, as tenant 1111…, for tenant 9999…'s rules.
        EvaluateDiscountRulesResponse spoofed = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = OtherRestaurantGuid.ToString(),
                OrderTotal = 500.00d,
            },
            ReadMetadata());

        spoofed.ApplicableCouponIds.Should().BeEmpty(
            "the global query filter scopes DiscountRule to the caller's tenant claim, so naming "
            + "another tenant's restaurant id in the request cannot reach their rules");
        spoofed.ApplicableCouponIds.Should().NotContain(otherTenantCouponId);

        // Sanity check: the caller's own tenant still evaluates normally, so
        // the assertion above is isolation and not a blanket empty response.
        int ownCouponId = await SeedRuleAsync(
            TestRestaurantGuid,
            DiscountRuleKind.MinOrderAmount,
            """{"MinOrderAmount":"10.00"}""");

        EvaluateDiscountRulesResponse own = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                OrderTotal = 500.00d,
            },
            ReadMetadata());

        own.ApplicableCouponIds.Should().Contain(ownCouponId);
    }

    [Fact]
    public async Task EvaluateRules_UnparseableRestaurantId_ReturnsEmptyWithoutThrowing()
    {
        await factory.CleanAllAsync();
        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        EvaluateDiscountRulesResponse response = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = "not-a-guid",
                OrderTotal = 75.00d,
            },
            ReadMetadata());

        response.ApplicableCouponIds.Should().BeEmpty(
            "a malformed restaurant id short-circuits to an empty response, never an RpcException");
    }
}
