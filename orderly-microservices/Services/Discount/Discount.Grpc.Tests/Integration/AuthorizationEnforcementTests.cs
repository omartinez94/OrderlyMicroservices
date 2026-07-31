using Grpc.Net.Client;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Negative-path coverage for the <c>DiscountAuthorizationInterceptor</c>.
/// Every <c>[Permission]</c>-attributed RPC across all three gRPC services
/// (<see cref="Discount.Grpc.Services.DiscountService"/>,
/// <see cref="Discount.Grpc.Services.DiscountRuleService"/>,
/// <see cref="Discount.Grpc.Services.RewardCodeService"/>) must reject
/// tokenless / wrong-permission calls with <see cref="StatusCode.PermissionDenied"/>
/// — and the rejection must include a <c>required-permission</c> trailer
/// so the caller knows which claim they were missing.
/// </summary>
/// <remarks>
/// <para>This is the security-sensitive suite for Phase 3 of
/// <c>TRUST_ROOT_HARDENING_PLAN.md</c>. A regression here means an
/// unauthenticated caller can hit a protected RPC; the test factory's
/// <see cref="TestAuthHandler"/> returns <c>NoResult()</c> on a missing
/// <c>X-Test-User</c> header and the production <c>AddJwtAuthenticationWithDevFallback</c>
/// would similarly leave <c>HttpContext.User</c> anonymous — so the
/// "no metadata" case mirrors the production "missing JWT" posture.</para>
/// <para>The positive-path coverage (every method with the right
/// permission) lives in <see cref="RpcEndpointTests"/> and
/// <see cref="DiscountRuleServiceTests"/>. This suite is intentionally
/// scoped to the deny path so a regression on either the method map
/// (does the right permission gate this method?) or the assertion
/// (does the interceptor actually enforce?) surfaces here first.</para>
/// <para>Each deny test wraps the gRPC call in <c>Func&lt;Task&gt;</c>
/// via an <c>async</c> lambda because the generated <c>XxxAsync</c>
/// methods return <c>AsyncUnaryCall&lt;TResponse&gt;</c> (not
/// <c>Task&lt;TResponse&gt;</c>). <see cref="Grpc.Core.AsyncUnaryCall{TResponse}"/>
/// implements the awaitable pattern itself, so <c>await client.XxxAsync(...)</c>
/// yields the response directly; the lambda wrapper is what lets
/// FluentAssertions observe the <see cref="RpcException"/> the
/// interceptor throws synchronously through the awaiter.</para>
/// </remarks>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class AuthorizationEnforcementTests(DiscountWebApplicationFactory factory)
{
    private const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid TestUserId = new("33333333-3333-3333-3333-333333333333");

    private static TClient BuildClient<TClient>(DiscountWebApplicationFactory factory, Func<GrpcChannel, TClient> factory2)
        where TClient : ClientBase<TClient>
    {
        var address = factory.ClientOptions.BaseAddress
            ?? throw new InvalidOperationException("WebApplicationFactory.BaseAddress is null");
        var httpClient = factory.CreateClient();
        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient,
        });
        return factory2(channel);
    }

    private static Metadata MetadataWithNoUser() => new();

    private static Metadata MetadataWithUserAnd(params string[] permissions)
    {
        // Authenticated caller but with the specified (possibly empty)
        // permission set. Used to drive "authenticated but wrong
        // permission" assertions.
        return new Metadata
        {
            { "x-test-user", TestUserId.ToString() },
            { "x-test-permissions", string.Join(",", permissions) },
        };
    }

    // -- DiscountService: every [Permission]-gated method must deny on no perms -------

    [Fact]
    public async Task GetDiscount_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.GetDiscountAsync(new GetDiscountRequest
            {
                RestaurantId = TestRestaurantId, Code = "ANY",
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponRead);
    }

    [Fact]
    public async Task CreateDiscount_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.CreateDiscountAsync(new CreateDiscountRequest
            {
                Coupon = new CouponModel { RestaurantId = TestRestaurantId, Code = "ANY" },
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponCreate);
    }

    [Fact]
    public async Task UpdateDiscount_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.UpdateDiscountAsync(new UpdateDiscountRequest
            {
                Coupon = new CouponModel { RestaurantId = TestRestaurantId, Code = "ANY", Id = 1 },
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponEdit);
    }

    [Fact]
    public async Task DeleteDiscount_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.DeleteDiscountAsync(new DeleteDiscountRequest
            {
                RestaurantId = TestRestaurantId, Code = "ANY",
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponDelete);
    }

    [Fact]
    public async Task RedeemDiscount_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.RedeemDiscountAsync(new RedeemDiscountRequest
            {
                RestaurantId = TestRestaurantId, Code = "ANY",
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponRedeem);
    }

    [Fact]
    public async Task ListDiscounts_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.ListDiscountsAsync(new ListDiscountsRequest
            {
                RestaurantId = TestRestaurantId, Page = 1, PageSize = 10,
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponRead);
    }

    // -- DiscountService: authenticated-but-wrong-permission denies ------------------

    [Fact]
    public async Task CreateDiscount_WrongPermission_Denies()
    {
        // Caller has coupon:read but the method requires coupon:create —
        // the read grant does NOT leak into the write path.
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.CreateDiscountAsync(new CreateDiscountRequest
            {
                Coupon = new CouponModel { RestaurantId = TestRestaurantId, Code = "ANY" },
            }, MetadataWithUserAnd(DiscountPermissions.CouponRead));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.CouponCreate);
    }

    // -- DiscountRuleService: reflects into the same map ---------------------------

    [Fact]
    public async Task GetDiscountRule_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountRuleProtoService.DiscountRuleProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.GetDiscountRuleAsync(new GetDiscountRuleRequest
            {
                RestaurantId = TestRestaurantId, RuleId = 1,
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.DiscountRuleRead);
    }

    [Fact]
    public async Task CreateDiscountRule_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new DiscountRuleProtoService.DiscountRuleProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.CreateDiscountRuleAsync(new CreateDiscountRuleRequest
            {
                Rule = new DiscountRuleModel { RestaurantId = TestRestaurantId, CouponId = 1 },
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.DiscountRuleEdit);
    }

    // -- RewardCodeService: third service, same enforcement pattern ----------------

    [Fact]
    public async Task GetRewardCode_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new RewardCodeProtoService.RewardCodeProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.GetRewardCodeAsync(new GetRewardCodeRequest
            {
                RestaurantId = TestRestaurantId, RewardCodeId = 1,
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.RewardCodeRead);
    }

    [Fact]
    public async Task RedeemRewardCode_NoPermissions_Denies()
    {
        var client = BuildClient(factory, c => new RewardCodeProtoService.RewardCodeProtoServiceClient(c));
        Func<Task> act = async () =>
            await client.RedeemRewardCodeAsync(new RedeemRewardCodeRequest
            {
                RestaurantId = TestRestaurantId, RewardCodeId = 1,
            }, MetadataWithNoUser());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.StatusCode.Should().Be(StatusCode.PermissionDenied);
        ex.Which.Trailers.Should().Contain(t => t.Key == "required-permission" && t.Value == DiscountPermissions.RewardCodeRedeem);
    }

    // -- Positive-path sanity: a single happy-path call per service proves the
    //    "deny on missing permission" path is permission-specific, not
    //    blanket-deny. Belt-and-braces: the test factory's TestAuthHandler
    //    + TestGrpcAuthInterceptor may drift, so an end-to-end success
    //    case on each service is the regression sentinel for the auth bridge.

    [Fact]
    public async Task DiscountService_HappyWithPermission_Admits()
    {
        await factory.CleanAllAsync();
        var client = BuildClient(factory, c => new DiscountProtoService.DiscountProtoServiceClient(c));
        var response = await client.GetDiscountAsync(new GetDiscountRequest
        {
            RestaurantId = TestRestaurantId, Code = "DOES-NOT-EXIST",
        }, MetadataWithUserAnd(DiscountPermissions.CouponRead));

        // Does not throw — empty coupon for a code with no match, but
        // the call was admitted by the interceptor.
        response.Should().NotBeNull();
        response.Coupon.Code.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscountRuleService_HappyWithPermission_Admits()
    {
        await factory.CleanAllAsync();
        var client = BuildClient(factory, c => new DiscountRuleProtoService.DiscountRuleProtoServiceClient(c));
        var response = await client.ListDiscountRulesAsync(new ListDiscountRulesRequest
        {
            RestaurantId = TestRestaurantId, Page = 1, PageSize = 10,
        }, MetadataWithUserAnd(DiscountPermissions.DiscountRuleRead));

        response.Should().NotBeNull();
    }

    [Fact]
    public async Task RewardCodeService_HappyWithPermission_Admits()
    {
        await factory.CleanAllAsync();
        var client = BuildClient(factory, c => new RewardCodeProtoService.RewardCodeProtoServiceClient(c));
        var response = await client.GetRewardCodeAsync(new GetRewardCodeRequest
        {
            RestaurantId = TestRestaurantId, RewardCodeId = 1,
        }, MetadataWithUserAnd(DiscountPermissions.RewardCodeRead));

        response.Should().NotBeNull();
    }
}
