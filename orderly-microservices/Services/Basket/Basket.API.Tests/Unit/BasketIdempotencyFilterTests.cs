using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="BasketIdempotencyFilter"/>. Locks
/// the IETF <c>draft-ietf-httpapi-idempotency-key-header</c> contract:
/// UUID v4 regex, body-match replay, body-mismatch 422, tenant-scoped
/// Redis keys, fail-closed on Redis errors. Also covers the hot-reload
/// behaviour for the operator-owned <c>type</c> URI base URL.
/// </summary>
public sealed class BasketIdempotencyFilterTests
{
    private const string TestUserId = "11111111-1111-1111-1111-111111111111";
    private const string TestRestaurantId = "22222222-2222-2222-2222-222222222222";
    private const string ValidUuidV4 = "8e9f7c4a-2b1d-4e6a-b3f5-9c8e7d6f5a4b";
    private const string TestBody = """{"userId":"11111111-1111-1111-1111-111111111111","restaurantId":"22222222-2222-2222-2222-222222222222","firstName":"Ada","lastName":"Lovelace","emailAddress":"ada@example.com","addressLine":"1 Way","country":"UK","state":"London","zipCode":"WC1","cardName":"Ada","cardNumber":"4111111111111111","expiration":"12/30","cvv":"123","paymentMethod":1}""";

    [Fact]
    public async Task MissingIdempotencyKey_Returns400()
    {
        var filter = BuildFilter(redis: Substitute.For<IConnectionMultiplexer>());
        var ctx = BuildInvocationContext(idempotencyKey: null, body: TestBody);

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task EmptyIdempotencyKey_Returns400()
    {
        var filter = BuildFilter(redis: Substitute.For<IConnectionMultiplexer>());
        var ctx = BuildInvocationContext(idempotencyKey: "   ", body: TestBody);

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task MalformedIdempotencyKey_Returns400()
    {
        var filter = BuildFilter(redis: Substitute.For<IConnectionMultiplexer>());
        var ctx = BuildInvocationContext(idempotencyKey: "not-a-uuid", body: TestBody);

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task FirstRequest_RunsHandler_AndCachesResult()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        var handlerCalled = false;
        var filter = BuildFilter(redis);

        var ctx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody);

        var result = await filter.InvokeAsync(ctx, c =>
        {
            handlerCalled = true;
            c.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            c.HttpContext.Response.ContentType = "application/json";
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeTrue();

        await db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == BuildExpectedRedisKey(ValidUuidV4)),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ReplayWithSameBody_ReturnsCached200_AndShortCircuitsHandler()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var keyProvider = new FixedHmacKeyProvider("a]fixed-key-for-test");
        var options = Options.Create(new BasketIdempotencyOptions { SecretHex = "00" + new string('0', 62), Ttl = TimeSpan.FromHours(24) });
        var requestBodyBytes = Encoding.UTF8.GetBytes(TestBody);
        var rawBodyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(requestBodyBytes));
        var expectedFingerprint = keyProvider.Compute($"{TestUserId}|{TestRestaurantId}|{rawBodyHash}");

        var cachedEntry = new IdempotencyCacheEntry(
            StatusCode: StatusCodes.Status200OK,
            Body: """{"success":true,"message":"cached"}"""u8.ToArray(),
            ContentType: "application/json",
            BodyHash: expectedFingerprint,
            StoredAt: SystemClock.Instance.GetCurrentInstant());
        var cachedJson = JsonSerializer.Serialize(cachedEntry, new JsonSerializerOptions { PropertyNamingPolicy = null });
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(cachedJson);

        var handlerCalled = false;
        var filter = new BasketIdempotencyFilter(redis, keyProvider, options, TestProblemOptionsMonitor(), NullLogger<BasketIdempotencyFilter>.Instance);

        var ctx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody);

        var result = await filter.InvokeAsync(ctx, c =>
        {
            handlerCalled = true;
            c.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeFalse();
        result.Should().BeSameAs(Results.Empty);
        ctx.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.HttpContext.Response.Headers.Should().ContainKey("Idempotent-Replayed");
        ctx.HttpContext.Response.Headers["Idempotent-Replayed"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task ReplayWithDifferentBody_Returns422()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var keyProvider = new FixedHmacKeyProvider("a]fixed-key-for-test");
        var options = Options.Create(new BasketIdempotencyOptions { SecretHex = "00" + new string('0', 62), Ttl = TimeSpan.FromHours(24) });

        var cachedEntry = new IdempotencyCacheEntry(
            StatusCode: StatusCodes.Status200OK,
            Body: """{"success":true,"message":"cached"}"""u8.ToArray(),
            ContentType: "application/json",
            BodyHash: "DIFFERENT-FINGERPRINT-FROM-EXPECTED",
            StoredAt: SystemClock.Instance.GetCurrentInstant());
        var cachedJson = JsonSerializer.Serialize(cachedEntry, new JsonSerializerOptions { PropertyNamingPolicy = null });
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(cachedJson);

        var filter = new BasketIdempotencyFilter(redis, keyProvider, options, TestProblemOptionsMonitor(), NullLogger<BasketIdempotencyFilter>.Instance);
        var ctx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody);

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task CrossUserReuse_DoesNotCollide_BecauseRedisKeyIsTenantScoped()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var firstCtx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody, userId: TestUserId, restaurantId: TestRestaurantId);
        var filter = BuildFilter(redis);

        await filter.InvokeAsync(firstCtx, c =>
        {
            c.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            c.HttpContext.Response.ContentType = "application/json";
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        var otherUserId = "33333333-3333-3333-3333-333333333333";
        var secondCtx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody, userId: otherUserId, restaurantId: TestRestaurantId);

        var handlerCalled = false;
        await filter.InvokeAsync(secondCtx, c =>
        {
            handlerCalled = true;
            c.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            c.HttpContext.Response.ContentType = "application/json";
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RedisGetFailure_Returns503_AndDoesNotRunHandler()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect, "redis down"));

        var handlerCalled = false;
        var filter = BuildFilter(redis);

        var ctx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody);
        var result = await filter.InvokeAsync(ctx, c =>
        {
            handlerCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeFalse();
        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task MissingIdempotencyKey_TypeUri_UsesConfiguredBaseUrl()
    {
        // Locks the contract: the `type` URI in the 400 ProblemDetails
        // body is "{BaseUrl}missing-idempotency-key". The base URL is
        // hot-reloadable via Basket__Problems__BaseUrl — see
        // BasketProblemDetailsOptions + the Program.cs Bind.
        var monitor = new TestOptionsMonitor<BasketProblemDetailsOptions>(
            new BasketProblemDetailsOptions { BaseUrl = "https://docs.example/problems/" });

        var filter = new BasketIdempotencyFilter(
            Substitute.For<IConnectionMultiplexer>(),
            new FixedHmacKeyProvider("test"),
            Options.Create(new BasketIdempotencyOptions { SecretHex = "00" + new string('0', 62), Ttl = TimeSpan.FromHours(24) }),
            monitor,
            NullLogger<BasketIdempotencyFilter>.Instance);

        var ctx = BuildInvocationContext(idempotencyKey: null, body: TestBody);
        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(Results.Ok()));
        var http = await ExecuteResultAsync(result!);

        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var bodyText = await ReadBodyAsync(http);
        bodyText.Should().Contain("https://docs.example/problems/missing-idempotency-key");
    }

    [Fact]
    public async Task ProblemBaseUrl_HotReload_ReflectsChangesAcrossRequests()
    {
        // The point of IOptionsMonitor: changes to Basket:Problems:BaseUrl
        // (via env var or appsettings.json) propagate to the NEXT request
        // without a process restart. This test mutates the monitor's value
        // mid-test and asserts the second invocation sees the new URL —
        // proving the filter isn't caching the value at construction time.
        var monitor = new TestOptionsMonitor<BasketProblemDetailsOptions>(
            new BasketProblemDetailsOptions { BaseUrl = "https://old.example/problems/" });

        var filter = new BasketIdempotencyFilter(
            Substitute.For<IConnectionMultiplexer>(),
            new FixedHmacKeyProvider("test"),
            Options.Create(new BasketIdempotencyOptions { SecretHex = "00" + new string('0', 62), Ttl = TimeSpan.FromHours(24) }),
            monitor,
            NullLogger<BasketIdempotencyFilter>.Instance);

        // First request: old URL.
        var ctx1 = BuildInvocationContext(idempotencyKey: null, body: TestBody);
        var result1 = await filter.InvokeAsync(ctx1, _ => ValueTask.FromResult<object?>(Results.Ok()));
        var body1 = await ReadBodyAsync(await ExecuteResultAsync(result1!));
        body1.Should().Contain("https://old.example/problems/missing-idempotency-key");

        // Mutate the monitor — simulates a config reload (env var change,
        // appsettings.json edit, etc.). The change should be visible on
        // the NEXT invocation without re-constructing the filter.
        monitor.Set(new BasketProblemDetailsOptions { BaseUrl = "https://new.example/problems/" });

        // Second request: new URL.
        var ctx2 = BuildInvocationContext(idempotencyKey: null, body: TestBody);
        var result2 = await filter.InvokeAsync(ctx2, _ => ValueTask.FromResult<object?>(Results.Ok()));
        var body2 = await ReadBodyAsync(await ExecuteResultAsync(result2!));
        body2.Should().Contain("https://new.example/problems/missing-idempotency-key");
    }

    // ----------------------------------------------------------------------
    // Test helpers
    // ----------------------------------------------------------------------

    private static BasketIdempotencyFilter BuildFilter(IConnectionMultiplexer redis)
    {
        var keyProvider = new FixedHmacKeyProvider("a]fixed-key-for-test");
        var options = Options.Create(new BasketIdempotencyOptions
        {
            SecretHex = "00" + new string('0', 62),
            Ttl = TimeSpan.FromHours(24),
        });
        return new BasketIdempotencyFilter(
            redis,
            keyProvider,
            options,
            TestProblemOptionsMonitor(),
            NullLogger<BasketIdempotencyFilter>.Instance);
    }

    /// <summary>
    /// Test double for <see cref="IOptionsMonitor{TOptions}"/> — lets
    /// tests construct a <see cref="BasketIdempotencyFilter"/> without
    /// building a real config provider. Default value uses the
    /// placeholder base URL; tests can construct one with a custom URL
    /// via the explicit constructor.
    /// </summary>
    private static IOptionsMonitor<BasketProblemDetailsOptions> TestProblemOptionsMonitor(
        string? baseUrl = null)
        => new TestOptionsMonitor<BasketProblemDetailsOptions>(
            new BasketProblemDetailsOptions { BaseUrl = baseUrl ?? "https://orderly.io/problems/" });

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T initial) { CurrentValue = initial; }
        public T CurrentValue { get; private set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Set(T value) => CurrentValue = value;
    }

    private static EndpointFilterInvocationContext BuildInvocationContext(
        string? idempotencyKey,
        string body,
        string? userId = null,
        string? restaurantId = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/cart/checkout";
        http.Request.ContentType = "application/json";
        if (idempotencyKey is not null)
        {
            http.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        http.Request.Body = new MemoryStream(bodyBytes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId ?? TestUserId),
            new("restaurantId", restaurantId ?? TestRestaurantId),
        };
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        http.User = new ClaimsPrincipal(identity);

        var services = new ServiceCollection().BuildServiceProvider();
        return new DefaultEndpointFilterInvocationContext(http, services);
    }

    private static async Task<HttpContext> ExecuteResultAsync(object result)
    {
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        http.Response.Body = new MemoryStream();
        if (result is IResult r)
        {
            await r.ExecuteAsync(http);
        }
        else
        {
            throw new InvalidOperationException($"Expected IResult, got {result?.GetType()}");
        }
        return http;
    }

    private static async Task<string> ReadBodyAsync(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body).ReadToEndAsync();
    }

    private static string BuildExpectedRedisKey(string idempotencyKey)
        => $"basket:idem:{TestUserId}:{TestRestaurantId}:{idempotencyKey}";

    /// <summary>
    /// Deterministic test double for the key provider. Reads the secret
    /// from a fixed string rather than configuration so tests don't need
    /// to wire up IConfiguration + IOptions.
    /// </summary>
    private sealed class FixedHmacKeyProvider : IBasketIdempotencyKeyProvider
    {
        private readonly byte[] _key;

        public FixedHmacKeyProvider(string keyMaterial)
        {
            _key = Encoding.UTF8.GetBytes(keyMaterial);
        }

        public string Compute(string envelope)
        {
            var bytes = Encoding.UTF8.GetBytes(envelope);
            var mac = System.Security.Cryptography.HMACSHA256.HashData(_key, bytes);
            return Convert.ToHexString(mac);
        }
    }
}
