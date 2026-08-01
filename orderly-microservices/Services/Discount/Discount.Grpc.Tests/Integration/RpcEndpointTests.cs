using Grpc.Net.Client;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// First gRPC integration tests in the monorepo — drives
/// <see cref="DiscountProtoService.DiscountProtoServiceClient"/> against
/// <see cref="DiscountWebApplicationFactory"/> over HTTP/2 with
/// <see cref="TestGrpcAuthInterceptor"/> stand-in for the JWT bearer.
/// Plan §v1.3 H-L19: WebApplicationFactory&lt;Program&gt; + Grpc.Net.Client +
/// per-RPC negative-path assertions. The interceptor stack is
/// <c>TestGrpcAuthInterceptor</c> (sets <c>HttpContext.User</c> from
/// <c>x-test-user</c> + <c>x-test-permissions</c> metadata) →
/// <c>DiscountAuthorizationInterceptor</c> (enforces per-method permission
/// policies from the method-path → permission map built by
/// <see cref="Discount.Grpc.Authorization.AuthorizationPolicies"/>).
/// </summary>
/// <remarks>
/// <para>
/// Each test mints a <see cref="Metadata"/> with two keys:
/// </para>
/// <list type="bullet">
/// <item><c>x-test-user</c> — a Guid identifying the caller (read by
/// <see cref="TestGrpcAuthInterceptor"/>).</item>
/// <item><c>x-test-permissions</c> — comma-separated permission strings
/// granted to the caller (e.g. <c>coupon:read, coupon:create</c>).</item>
/// </list>
/// <para>
/// Without the matching permission on a method, the call returns
/// <c>StatusCode.PermissionDenied</c> with a <c>required-permission</c>
/// trailer — that's the <c>DiscountAuthorizationInterceptor</c> at work.
/// The AuthorizationEnforcementTests class is the negative-path suite;
/// these tests cover the happy paths and need the corresponding
/// <see cref="DiscountPermissions"/> strings granted to succeed.
/// </para>
/// </remarks>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class RpcEndpointTests(DiscountWebApplicationFactory factory)
{
    private const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid TestUserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestRestaurantGuid = new(TestRestaurantId);

    /// <summary>Builds the gRPC client pointed at the test factory's
    /// in-process server. The factory's <c>CreateClient()</c> returns
    /// an <see cref="HttpClient"/> wrapping the test server;
    /// <see cref="GrpcChannel.ForAddress(string,GrpcChannelOptions)"/>
    /// detects HTTP/2 from the test transport.</summary>
    private static DiscountProtoService.DiscountProtoServiceClient BuildClient(
        DiscountWebApplicationFactory factory)
    {
        var address = factory.ClientOptions.BaseAddress
            ?? throw new InvalidOperationException("WebApplicationFactory.BaseAddress is null");
        var httpClient = factory.CreateClient();
        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient,
        });
        return new DiscountProtoService.DiscountProtoServiceClient(channel);
    }

    private static Metadata BuildMetadata(
        Guid userId,
        params string[] permissions)
    {
        var md = new Metadata
        {
            { "x-test-user", userId.ToString() },
            { "x-test-permissions", string.Join(",", permissions) },
        };
        return md;
    }

    [Fact]
    public async Task GetDiscount_Happy_ReturnsCoupon()
    {
        // Arrange: clean + seed a known coupon, then call with the
        // coupon:read permission so the DiscountAuthorizationInterceptor
        // admits the call.
        await factory.CleanAllAsync();
        const string code = "RPC-GET-HAPPY";
        await factory.SeedCouponAsync(TestRestaurantGuid, code: code, amount: 15m);

        var client = BuildClient(factory);
        var request = new GetDiscountRequest
        {
            RestaurantId = TestRestaurantId,
            Code = code,
        };

        // Act
        var response = await client.GetDiscountAsync(request, BuildMetadata(
            TestUserId, DiscountPermissions.CouponRead));

        // Assert
        response.Coupon.Code.Should().Be(code);
        response.Coupon.Amount.Should().BeApproximately(15.0, 0.001);
        response.Coupon.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetDiscount_NotFound_ReturnsEmptyModel()
    {
        // Arrange: no seed. The gRPC contract returns an empty
        // (Code == "") CouponModel on miss; the request is still
        // admitted because the caller has coupon:read.
        await factory.CleanAllAsync();
        var client = BuildClient(factory);
        var request = new GetDiscountRequest
        {
            RestaurantId = TestRestaurantId,
            Code = "DOES-NOT-EXIST",
        };

        // Act
        var response = await client.GetDiscountAsync(request, BuildMetadata(
            TestUserId, DiscountPermissions.CouponRead));

        // Assert: gRPC service returns an empty coupon for "not found",
        // matching the production behaviour.
        response.Coupon.Code.Should().BeEmpty();
        response.Coupon.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ListDiscounts_PageDefaults_ReturnsPagedResults()
    {
        // Arrange: seed 3 coupons, request page 1 with default page size.
        await factory.CleanAllAsync();
        await factory.SeedCouponAsync(TestRestaurantGuid, code: "LIST-1");
        await factory.SeedCouponAsync(TestRestaurantGuid, code: "LIST-2");
        await factory.SeedCouponAsync(TestRestaurantGuid, code: "LIST-3");

        var client = BuildClient(factory);
        var request = new ListDiscountsRequest
        {
            RestaurantId = TestRestaurantId,
            Page = 1,
            PageSize = 0, // server defaults to 50
        };

        // Act
        var response = await client.ListDiscountsAsync(request, BuildMetadata(
            TestUserId, DiscountPermissions.CouponRead));

        // Assert: the global tenant filter scopes the query to the test
        // tenant; TotalCount reflects the same filter so callers can
        // page through exactly the rows they can see.
        response.TotalCount.Should().Be(3);
        response.Coupons.Should().HaveCount(3);
        response.Coupons.Select(c => c.Code).Should().BeEquivalentTo(
            new[] { "LIST-1", "LIST-2", "LIST-3" });
    }

    [Fact]
    public async Task RedeemDiscount_Happy_ReturnsSuccess()
    {
        // Arrange: seed an active coupon with no redemption cap.
        // The conditional UPDATE increments RedeemAmount by 1.
        await factory.CleanAllAsync();
        const string code = "RPC-REDEEM-HAPPY";
        await factory.SeedCouponAsync(TestRestaurantGuid, code: code, amount: 10m);

        var client = BuildClient(factory);

        // Act: redeem with the coupon:redeem permission.
        var response = await client.RedeemDiscountAsync(new RedeemDiscountRequest
        {
            RestaurantId = TestRestaurantId,
            Code = code,
        }, BuildMetadata(TestUserId, DiscountPermissions.CouponRedeem));

        // Assert
        response.Success.Should().BeTrue();

        // Verify the row's RedeemAmount incremented in the DB.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var coupon = await db.Coupons.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Code == code);
        coupon.RedeemAmount.Should().Be(1);
    }

    [Fact]
    public async Task CreateDiscount_Happy_ReturnsSuccessAndPersists()
    {
        // Arrange: clean baseline so the new coupon's PK doesn't collide.
        await factory.CleanAllAsync();
        var client = BuildClient(factory);
        const string code = "RPC-CREATE-HAPPY";
        var request = new CreateDiscountRequest
        {
            Coupon = new CouponModel
            {
                RestaurantId = TestRestaurantId,
                Code = code,
                Description = "created via gRPC integration test",
                Amount = 7.5,
                IsActive = true,
            },
        };

        // Act
        var response = await client.CreateDiscountAsync(request, BuildMetadata(
            TestUserId, DiscountPermissions.CouponCreate));

        // Assert
        response.Success.Should().BeTrue();
        response.Coupon.Code.Should().Be(code);

        // Verify the row exists in the DB with the assigned Id.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.Coupons.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code);
        stored.Should().NotBeNull();
        stored!.RestaurantId.Should().Be(TestRestaurantGuid);
    }

    [Fact]
    public async Task DeleteDiscount_Happy_RemovesCoupon()
    {
        // Arrange: seed then delete via gRPC.
        await factory.CleanAllAsync();
        const string code = "RPC-DELETE-HAPPY";
        await factory.SeedCouponAsync(TestRestaurantGuid, code: code, amount: 5m);

        var client = BuildClient(factory);

        // Act: delete with the coupon:delete permission.
        var response = await client.DeleteDiscountAsync(new DeleteDiscountRequest
        {
            RestaurantId = TestRestaurantId,
            Code = code,
        }, BuildMetadata(TestUserId, DiscountPermissions.CouponDelete));

        // Assert
        response.Success.Should().BeTrue();

        // Verify the row is gone (Coupons hard-delete; soft-delete is
        // the DiscountRule + RewardCode path).
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.Coupons.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code);
        stored.Should().BeNull("Coupon is hard-deleted (vs. DiscountRule/RewardCode which soft-delete)");
    }
}
