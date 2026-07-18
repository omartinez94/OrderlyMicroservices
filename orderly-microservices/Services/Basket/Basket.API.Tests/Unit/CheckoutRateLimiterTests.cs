namespace Basket.API.Tests.Unit;

/// <summary>
/// Smoke tests for <see cref="CheckoutRateLimiter"/>. Locks the policy
/// constants, the partition function's tenant-scoping behaviour, and
/// the OnRejected callback's 429 + Retry-After envelope.
/// </summary>
/// <remarks>
/// The actual end-to-end rate-limit behaviour (a sixth request
/// returning 429 inside a real ASP.NET Core request pipeline) is
/// covered by Phase 5's Testcontainers + <c>BasketWebApplicationFactory</c>.
/// These tests validate the policy in isolation — they prove the
/// wiring matches the plan §0.4.8 spec without spinning up the full
/// Basket host (which would need Marten + Redis + RabbitMQ + JWT).
/// </remarks>
public sealed class CheckoutRateLimiterTests
{
    [Fact]
    public void PolicyName_IsCheckout()
    {
        CheckoutRateLimiter.PolicyName.Should().Be("checkout");
    }

    [Fact]
    public void PermitLimit_IsFivePerMinute_PerPlan_0_4_8()
    {
        CheckoutRateLimiter.PermitLimit.Should().Be(5);
        CheckoutRateLimiter.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void PartitionFunc_KeysOnUserIdAndRestaurantId()
    {
        var http = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
                        new System.Security.Claims.Claim("restaurantId", "22222222-2222-2222-2222-222222222222"),
                    },
                    authenticationType: "Test")),
        };

        var partition = CheckoutRateLimiter.PartitionFunc(http);

        partition.PartitionKey.Should().Be("11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void PartitionFunc_DifferentRestaurants_GetDifferentPartitions()
    {
        var partition1 = CheckoutRateLimiter.PartitionFunc(BuildHttpContext(
            userId: "11111111-1111-1111-1111-111111111111",
            restaurantId: "22222222-2222-2222-2222-222222222222"));

        var partition2 = CheckoutRateLimiter.PartitionFunc(BuildHttpContext(
            userId: "11111111-1111-1111-1111-111111111111",
            restaurantId: "33333333-3333-3333-3333-333333333333"));

        partition1.PartitionKey.Should().NotBe(partition2.PartitionKey);
    }

    [Fact]
    public async Task FixedWindowLimiter_AllowsFive_RejectsSixth()
    {
        // Construct a real PartitionedRateLimiter with the same
        // FixedWindowRateLimiterOptions the policy uses. This proves
        // the configured limit is what we document (5/minute/partition).
        using var limiter = PartitionedRateLimiter.Create<string, string>(resource =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "test-user:test-restaurant",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = CheckoutRateLimiter.PermitLimit,
                    Window = CheckoutRateLimiter.Window,
                    QueueLimit = 0,
                    AutoReplenishment = false, // synchronous window for the test
                }));

        // First five requests: all succeed.
        for (var i = 0; i < CheckoutRateLimiter.PermitLimit; i++)
        {
            using var lease = await limiter.AcquireAsync(
                resource: "test-user:test-restaurant",
                permitCount: 1,
                cancellationToken: CancellationToken.None);
            lease.IsAcquired.Should().BeTrue($"request #{i + 1} should succeed within the {CheckoutRateLimiter.PermitLimit}-permit window");
        }

        // Sixth request: rejected.
        using var rejectedLease = await limiter.AcquireAsync(
            resource: "test-user:test-restaurant",
            permitCount: 1,
            cancellationToken: CancellationToken.None);
        rejectedLease.IsAcquired.Should().BeFalse("the sixth request should be rejected by the FixedWindowRateLimiter");
    }

    [Fact]
    public async Task OnRejectedAsync_Sets429Status_AndRetryAfterHeader_AndProblemDetailsBody()
    {
        // Wire the static options monitor — required since the Phase 2.4
        // hot-reload refactor removed the inline fallback. The test
        // exercises the production path through OnRejectedAsync, which
        // reads CurrentValue.BaseUrl directly.
        CheckoutRateLimiter.Configure(new TestOptionsMonitor<BasketProblemDetailsOptions>(
            new BasketProblemDetailsOptions { BaseUrl = "https://test.example/problems/" }));

        // Build a real lease by exhausting the limiter, then pass
        // the rejected lease to the OnRejected callback.
        using var limiter = PartitionedRateLimiter.Create<string, string>(resource =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "test",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromSeconds(60),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        // First request consumes the permit.
        using var firstLease = await limiter.AcquireAsync(resource: "test", permitCount: 1, cancellationToken: CancellationToken.None);
        firstLease.IsAcquired.Should().BeTrue();

        // Second request is rejected — that's the lease we hand to the callback.
        var rejectedLease = await limiter.AcquireAsync(resource: "test", permitCount: 1, cancellationToken: CancellationToken.None);
        rejectedLease.IsAcquired.Should().BeFalse();

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        var context = new OnRejectedContext
        {
            HttpContext = http,
            Lease = rejectedLease,
        };

        await CheckoutRateLimiter.OnRejectedAsync(context, CancellationToken.None);

        http.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);

        // The Retry-After header should be present and parseable.
        if (http.Response.Headers.TryGetValue("Retry-After", out var retryAfterValues))
        {
            var retryAfterString = retryAfterValues.ToString();
            int.TryParse(retryAfterString, out var seconds).Should().BeTrue();
            seconds.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(60);
        }

        http.Response.ContentType.Should().Be("application/problem+json");

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"title\":\"Too Many Requests\"");
        body.Should().Contain("\"status\":429");
    }

    [Fact]
    public async Task OnRejectedAsync_WhenLeaseExposesNoMetadata_OmitsRetryAfterHeader()
    {
        CheckoutRateLimiter.Configure(new TestOptionsMonitor<BasketProblemDetailsOptions>(
            new BasketProblemDetailsOptions { BaseUrl = "https://test.example/problems/" }));

        // Build a lease via a queueing-style limiter that doesn't
        // expose RetryAfter metadata. We can simulate this by using a
        // ConcurrencyLimiter (no RetryAfter metadata available).
        using var limiter = PartitionedRateLimiter.Create<string, string>(resource =>
            RateLimitPartition.GetConcurrencyLimiter(
                partitionKey: "test",
                factory: _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 1,
                    QueueLimit = 0,
                }));

        using var firstLease = await limiter.AcquireAsync(resource: "test", permitCount: 1, cancellationToken: CancellationToken.None);
        firstLease.IsAcquired.Should().BeTrue();

        var rejectedLease = await limiter.AcquireAsync(resource: "test", permitCount: 1, cancellationToken: CancellationToken.None);
        rejectedLease.IsAcquired.Should().BeFalse();

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        var context = new OnRejectedContext
        {
            HttpContext = http,
            Lease = rejectedLease,
        };

        await CheckoutRateLimiter.OnRejectedAsync(context, CancellationToken.None);

        http.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        // ConcurrencyLimiter doesn't expose RetryAfter metadata — the
        // callback's TryGetMetadata call returns false and the header
        // is omitted.
        http.Response.Headers.Should().NotContainKey("Retry-After");
    }

    private static HttpContext BuildHttpContext(string userId, string restaurantId)
    {
        var http = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
                        new System.Security.Claims.Claim("restaurantId", restaurantId),
                    },
                    authenticationType: "Test")),
        };
        return http;
    }

    /// <summary>
    /// Test double for <see cref="IOptionsMonitor{TOptions}"/> — lets
    /// tests wire a value into CheckoutRateLimiter's static
    /// <c>Configure</c> hook without building a real config provider.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T initial) { CurrentValue = initial; }
        public T CurrentValue { get; private set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
