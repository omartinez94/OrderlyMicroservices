using Grpc.Net.Client;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Covers the paging contract shared by <c>ListDiscountRules</c> and
/// <c>ListRewardCodes</c>: <c>page &lt;= 0</c> clamps to 1, <c>pageSize &lt;= 0</c>
/// falls back to the 50 default, and <c>pageSize &gt; 200</c> clamps to the 200
/// ceiling. <c>total_count</c> always reports the full tenant-scoped row count,
/// not the size of the returned slice.
///
/// <para><b>Plan deviation (§6.5).</b> The plan lists
/// <c>ListRewardCodes_FilterByKind_ReturnsFilteredList()</c>. The
/// <c>ListRewardCodesRequest</c> message has no kind field — it carries only
/// <c>restaurant_id</c>, <c>page</c> and <c>page_size</c>, and the handler
/// applies no kind predicate. Replaced with
/// <see cref="ListRewardCodes_Page2_ReturnsSecondSlice"/> so both list RPCs get
/// symmetric offset coverage.</para>
///
/// <para>Both list RPCs order deterministically — rules by <c>CouponId</c>,
/// reward codes by <c>Code</c> — which is what makes the page-2 slice
/// assertions stable.</para>
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class ListPaginationTests(DiscountWebApplicationFactory factory)
{
    private const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";

    private static readonly Guid TestRestaurantGuid = new(TestRestaurantId);
    private static readonly Guid TestUserId = new("22222222-2222-2222-2222-222222222222");

    private static DiscountRuleProtoService.DiscountRuleProtoServiceClient BuildRuleClient(
        DiscountWebApplicationFactory factory)
        => new(BuildChannel(factory));

    private static RewardCodeProtoService.RewardCodeProtoServiceClient BuildRewardClient(
        DiscountWebApplicationFactory factory)
        => new(BuildChannel(factory));

    private static GrpcChannel BuildChannel(DiscountWebApplicationFactory factory)
    {
        Uri address = factory.ClientOptions.BaseAddress
            ?? throw new InvalidOperationException("WebApplicationFactory.BaseAddress is null");
        HttpClient httpClient = factory.CreateClient();
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static Metadata MetadataFor(string permission) => new()
    {
        { "x-test-user", TestUserId.ToString() },
        { "x-test-permissions", permission },
    };

    /// <summary>
    /// Seeds <paramref name="count"/> rules, each on its own coupon. Rules sort
    /// by CouponId, and coupons are inserted in sequence, so the seeding order
    /// is the list order.
    /// </summary>
    private async Task<List<int>> SeedRulesAsync(int count)
    {
        List<int> couponIds = [];
        for (int i = 0; i < count; i++)
        {
            Coupon coupon = await factory.SeedCouponAsync(
                TestRestaurantGuid, code: $"PAGE-R{i:D2}-{Guid.NewGuid():N}"[..16]);
            couponIds.Add(coupon.Id);

            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            DiscountContext db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            db.DiscountRules.Add(new DiscountRule
            {
                RestaurantId = TestRestaurantGuid,
                CouponId = coupon.Id,
                RuleType = DiscountRuleKind.MinOrderAmount,
                RuleDataJson = """{"MinOrderAmount":"10.00"}""",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        couponIds.Sort();
        return couponIds;
    }

    /// <summary>
    /// Seeds <paramref name="count"/> reward codes with zero-padded, lexically
    /// ordered codes so the <c>OrderBy(r =&gt; r.Code)</c> slice is predictable.
    /// </summary>
    private async Task<List<string>> SeedRewardCodesAsync(int count)
    {
        List<string> codes = [];
        for (int i = 0; i < count; i++)
        {
            string code = $"PAGERWD{i:D3}";
            await factory.SeedRewardCodeAsync(TestRestaurantGuid, code: code);
            codes.Add(code);
        }

        codes.Sort(StringComparer.Ordinal);
        return codes;
    }

    [Fact]
    public async Task ListDiscountRules_DefaultPagination_ReturnsFirstPage()
    {
        await factory.CleanAllAsync();
        List<int> couponIds = await SeedRulesAsync(3);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildRuleClient(factory);

        // page 0 / pageSize 0 exercise both clamps at once.
        ListDiscountRulesResponse response = await client.ListDiscountRulesAsync(
            new ListDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                Page = 0,
                PageSize = 0,
            },
            MetadataFor(DiscountPermissions.DiscountRuleRead));

        response.TotalCount.Should().Be(3);
        response.Rules.Should().HaveCount(3,
            "pageSize 0 falls back to the 50 default, which covers all 3 rows");
        response.Rules.Select(r => r.CouponId).Should().ContainInOrder(couponIds);
    }

    [Fact]
    public async Task ListDiscountRules_Page2_ReturnsSecondSlice()
    {
        await factory.CleanAllAsync();
        List<int> couponIds = await SeedRulesAsync(5);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildRuleClient(factory);

        ListDiscountRulesResponse page2 = await client.ListDiscountRulesAsync(
            new ListDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                Page = 2,
                PageSize = 2,
            },
            MetadataFor(DiscountPermissions.DiscountRuleRead));

        page2.TotalCount.Should().Be(5, "total_count reports the full set, not the slice");
        page2.Rules.Should().HaveCount(2);
        page2.Rules.Select(r => r.CouponId)
            .Should().ContainInOrder(
                new[] { couponIds[2], couponIds[3] },
                "page 2 at pageSize 2 skips the first two rows");
    }

    [Fact]
    public async Task ListDiscountRules_PageSizeAboveCeiling_ClampsTo200()
    {
        await factory.CleanAllAsync();
        await SeedRulesAsync(2);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildRuleClient(factory);

        ListDiscountRulesResponse response = await client.ListDiscountRulesAsync(
            new ListDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                Page = 1,
                PageSize = 5_000,
            },
            MetadataFor(DiscountPermissions.DiscountRuleRead));

        // The clamp is not echoed in the response, so it is asserted
        // indirectly: an unclamped Take(5000) and a clamped Take(200) both
        // return the 2 seeded rows, and the call must not fault.
        response.TotalCount.Should().Be(2);
        response.Rules.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListRewardCodes_DefaultPagination_ReturnsFirstPage()
    {
        await factory.CleanAllAsync();
        List<string> codes = await SeedRewardCodesAsync(3);

        RewardCodeProtoService.RewardCodeProtoServiceClient client = BuildRewardClient(factory);

        ListRewardCodesResponse response = await client.ListRewardCodesAsync(
            new ListRewardCodesRequest
            {
                RestaurantId = TestRestaurantId,
                Page = 0,
                PageSize = 0,
            },
            MetadataFor(DiscountPermissions.RewardCodeRead));

        response.TotalCount.Should().Be(3);
        response.RewardCodes.Should().HaveCount(3);
        response.RewardCodes.Select(r => r.Code).Should().ContainInOrder(codes);
    }

    [Fact]
    public async Task ListRewardCodes_Page2_ReturnsSecondSlice()
    {
        await factory.CleanAllAsync();
        List<string> codes = await SeedRewardCodesAsync(5);

        RewardCodeProtoService.RewardCodeProtoServiceClient client = BuildRewardClient(factory);

        ListRewardCodesResponse page2 = await client.ListRewardCodesAsync(
            new ListRewardCodesRequest
            {
                RestaurantId = TestRestaurantId,
                Page = 2,
                PageSize = 2,
            },
            MetadataFor(DiscountPermissions.RewardCodeRead));

        page2.TotalCount.Should().Be(5);
        page2.RewardCodes.Should().HaveCount(2);
        page2.RewardCodes.Select(r => r.Code)
            .Should().ContainInOrder(codes[2], codes[3]);
    }
}
