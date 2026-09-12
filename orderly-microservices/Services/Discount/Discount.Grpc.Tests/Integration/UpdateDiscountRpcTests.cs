using Grpc.Net.Client;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Covers <c>DiscountRuleProtoService.UpdateDiscountRule</c>.
///
/// <para><b>Plan deviations (§6.5).</b> Two, both because the plan's names
/// assume a gRPC status-code contract the service does not implement:</para>
/// <list type="number">
/// <item><c>UpdateDiscount_NonExistent_ReturnsNotFoundStatus()</c> — the
/// handler returns <c>UpdateDiscountRuleResponse { Success = false }</c> on a
/// missing row. It does NOT throw <c>RpcException(StatusCode.NotFound)</c>.
/// The test asserts the real success-flag contract.</item>
/// <item><c>UpdateDiscount_InvalidFields_ReturnsInvalidArgument()</c> — there
/// is no request validation on this RPC at all: <c>rule_data_json</c> is
/// persisted verbatim, malformed or not, and the evaluator is the component
/// that skips unparseable payloads at read time. Replaced with
/// <see cref="UpdateDiscountRule_MalformedRuleDataJson_PersistsButNeverMatches"/>,
/// which pins that end-to-end behaviour instead of asserting a status code
/// that never fires.</item>
/// </list>
///
/// <para>The plan also names this RPC <c>UpdateDiscount</c>. That name belongs
/// to the coupon RPC on <c>DiscountProtoService</c>; the rule-side gap §6.5
/// describes is <c>UpdateDiscountRule</c>, which is what this file covers.</para>
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class UpdateDiscountRpcTests(DiscountWebApplicationFactory factory)
{
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

    private static Metadata EditMetadata() => new()
    {
        { "x-test-user", TestUserId.ToString() },
        { "x-test-permissions", DiscountPermissions.DiscountRuleEdit },
    };

    private static Metadata ReadMetadata() => new()
    {
        { "x-test-user", TestUserId.ToString() },
        { "x-test-permissions", DiscountPermissions.DiscountRuleRead },
    };

    private async Task<(int RuleId, int CouponId)> SeedRuleAsync(
        Guid restaurantId,
        DiscountRuleKind kind = DiscountRuleKind.MinOrderAmount,
        string ruleDataJson = """{"MinOrderAmount":"50.00"}""")
    {
        Coupon coupon = await factory.SeedCouponAsync(
            restaurantId, code: $"UPD-{Guid.NewGuid():N}"[..16]);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DiscountContext db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        DiscountRule rule = new DiscountRule
        {
            RestaurantId = restaurantId,
            CouponId = coupon.Id,
            RuleType = kind,
            RuleDataJson = ruleDataJson,
            IsActive = true,
        };
        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync();

        return (rule.Id, coupon.Id);
    }

    private async Task<DiscountRule?> ReadRuleIgnoringFiltersAsync(int ruleId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DiscountContext db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        return await db.DiscountRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId);
    }

    [Fact]
    public async Task UpdateDiscountRule_ValidRequest_ReturnsUpdatedRule()
    {
        await factory.CleanAllAsync();
        (int ruleId, int couponId) = await SeedRuleAsync(TestRestaurantGuid);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        UpdateDiscountRuleResponse response = await client.UpdateDiscountRuleAsync(
            new UpdateDiscountRuleRequest
            {
                Rule = new DiscountRuleModel
                {
                    Id = ruleId,
                    RestaurantId = TestRestaurantId,
                    CouponId = couponId,
                    RuleType = DiscountRuleType.Bogo,
                    RuleDataJson = """{"BuyQuantity":2,"GetQuantity":1}""",
                    IsActive = false,
                },
            },
            EditMetadata());

        response.Success.Should().BeTrue();
        response.Rule.RuleType.Should().Be(DiscountRuleType.Bogo);
        response.Rule.RuleDataJson.Should().Be("""{"BuyQuantity":2,"GetQuantity":1}""");
        response.Rule.IsActive.Should().BeFalse();

        DiscountRule? persisted = await ReadRuleIgnoringFiltersAsync(ruleId);
        persisted.Should().NotBeNull();
        persisted!.RuleType.Should().Be(DiscountRuleKind.Bogo, "the mutation must reach the database");
        persisted.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDiscountRule_NonExistent_ReturnsSuccessFalse()
    {
        await factory.CleanAllAsync();
        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        UpdateDiscountRuleResponse response = await client.UpdateDiscountRuleAsync(
            new UpdateDiscountRuleRequest
            {
                Rule = new DiscountRuleModel
                {
                    Id = 999_999,
                    RestaurantId = TestRestaurantId,
                    CouponId = 1,
                    RuleType = DiscountRuleType.MinOrderAmount,
                    RuleDataJson = "{}",
                    IsActive = true,
                },
            },
            EditMetadata());

        response.Success.Should().BeFalse(
            "a missing row returns Success=false; the RPC does not throw StatusCode.NotFound");
    }

    [Fact]
    public async Task UpdateDiscountRule_CrossTenant_CannotMutateOtherTenantsRule()
    {
        // Regression test for the Phase 5 tenant-filter fix. Before it, the
        // handler's FirstOrDefaultAsync(r => r.Id == id) lookup was unscoped,
        // so knowing another tenant's rule id was enough to overwrite it.
        await factory.CleanAllAsync();
        (int foreignRuleId, int foreignCouponId) = await SeedRuleAsync(OtherRestaurantGuid);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        UpdateDiscountRuleResponse response = await client.UpdateDiscountRuleAsync(
            new UpdateDiscountRuleRequest
            {
                Rule = new DiscountRuleModel
                {
                    Id = foreignRuleId,
                    RestaurantId = OtherRestaurantGuid.ToString(),
                    CouponId = foreignCouponId,
                    RuleType = DiscountRuleType.Bogo,
                    RuleDataJson = """{"BuyQuantity":9,"GetQuantity":9}""",
                    IsActive = false,
                },
            },
            EditMetadata());

        response.Success.Should().BeFalse(
            "the tenant-scoped lookup finds no row, so the update reports failure");

        DiscountRule? untouched = await ReadRuleIgnoringFiltersAsync(foreignRuleId);
        untouched.Should().NotBeNull();
        untouched!.RuleType.Should().Be(DiscountRuleKind.MinOrderAmount,
            "the other tenant's rule must be byte-for-byte unchanged");
        untouched.IsActive.Should().BeTrue();
        untouched.RuleDataJson.Should().Be("""{"MinOrderAmount":"50.00"}""");
    }

    [Fact]
    public async Task UpdateDiscountRule_MalformedRuleDataJson_PersistsButNeverMatches()
    {
        // There is no request-side validation: the malformed payload is stored
        // verbatim and returns Success=true. The evaluator is what defends —
        // JsonException is caught and the rule is skipped, so the coupon never
        // becomes applicable.
        await factory.CleanAllAsync();
        (int ruleId, int couponId) = await SeedRuleAsync(TestRestaurantGuid);

        DiscountRuleProtoService.DiscountRuleProtoServiceClient client = BuildClient(factory);

        UpdateDiscountRuleResponse response = await client.UpdateDiscountRuleAsync(
            new UpdateDiscountRuleRequest
            {
                Rule = new DiscountRuleModel
                {
                    Id = ruleId,
                    RestaurantId = TestRestaurantId,
                    CouponId = couponId,
                    RuleType = DiscountRuleType.MinOrderAmount,
                    RuleDataJson = "{ this is not json",
                    IsActive = true,
                },
            },
            EditMetadata());

        response.Success.Should().BeTrue(
            "the RPC performs no JSON validation — it persists whatever it is given");

        DiscountRule? persisted = await ReadRuleIgnoringFiltersAsync(ruleId);
        persisted!.RuleDataJson.Should().Be("{ this is not json");

        EvaluateDiscountRulesResponse evaluated = await client.EvaluateDiscountRulesAsync(
            new EvaluateDiscountRulesRequest
            {
                RestaurantId = TestRestaurantId,
                OrderTotal = 10_000d,
            },
            ReadMetadata());

        evaluated.ApplicableCouponIds.Should().NotContain(couponId,
            "the evaluator catches JsonException and skips the rule rather than failing the call");
    }
}
