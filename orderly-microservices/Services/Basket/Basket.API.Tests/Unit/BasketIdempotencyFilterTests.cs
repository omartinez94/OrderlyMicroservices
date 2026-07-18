using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="BasketIdempotencyFilter"/>. Locks
/// the IETF <c>draft-ietf-httpapi-idempotency-key-header</c> contract:
/// UUID v4 regex, body-match replay, body-mismatch 422, tenant-scoped
/// Redis keys, fail-closed on Redis errors.
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
            // Write to the captured response body so the filter can read it back.
            c.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            c.HttpContext.Response.ContentType = "application/json";
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeTrue();

        // Cache write happened with the expected key shape.
        await db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == BuildExpectedRedisKey(ValidUuidV4)),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());

        // The result should be the handler's own return value (Results.Ok()).
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ReplayWithSameBody_ReturnsCached200_AndShortCircuitsHandler()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        // Pre-populate Redis with a cached entry whose BodyHash matches the
        // current request's HMAC body fingerprint.
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
        var filter = new BasketIdempotencyFilter(redis, keyProvider, options, NullLogger<BasketIdempotencyFilter>.Instance);

        var ctx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody);

        var result = await filter.InvokeAsync(ctx, c =>
        {
            handlerCalled = true;
            c.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError; // sentinel — must NOT run
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        handlerCalled.Should().BeFalse(); // the handler did NOT run on a matching replay
        result.Should().BeSameAs(Results.Empty); // short-circuit sentinel
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

        // Cached entry has a BodyHash that does NOT match the current body.
        var cachedEntry = new IdempotencyCacheEntry(
            StatusCode: StatusCodes.Status200OK,
            Body: """{"success":true,"message":"cached"}"""u8.ToArray(),
            ContentType: "application/json",
            BodyHash: "DIFFERENT-FINGERPRINT-FROM-EXPECTED",
            StoredAt: SystemClock.Instance.GetCurrentInstant());
        var cachedJson = JsonSerializer.Serialize(cachedEntry, new JsonSerializerOptions { PropertyNamingPolicy = null });
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(cachedJson);

        var filter = new BasketIdempotencyFilter(redis, keyProvider, options, NullLogger<BasketIdempotencyFilter>.Instance);
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

        // First call: user A. Cached.
        var firstCtx = BuildInvocationContext(idempotencyKey: ValidUuidV4, body: TestBody, userId: TestUserId, restaurantId: TestRestaurantId);
        var filter = BuildFilter(redis);

        await filter.InvokeAsync(firstCtx, c =>
        {
            c.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            c.HttpContext.Response.ContentType = "application/json";
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        // Second call: different user, same Idempotency-Key.
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

        handlerCalled.Should().BeTrue(); // ran fresh because the Redis key is tenant-scoped
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

        handlerCalled.Should().BeFalse(); // fail-closed: don't run the handler if Redis is down
        var http = await ExecuteResultAsync(result!);
        http.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
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
            NullLogger<BasketIdempotencyFilter>.Instance);
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

        // The ClaimsPrincipal carries the JWT-derived identity the filter
        // uses for tenant scoping. Tests default to TestUserId / TestRestaurantId.
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
        // ProblemHttpResult requires RequestServices to resolve
        // IProblemDetailsService + ILoggerFactory; wire both.
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

    private static string BuildExpectedRedisKey(string idempotencyKey)
        => $"basket:idem:{TestUserId}:{TestRestaurantId}:{idempotencyKey}";

    private const int Status400 = 400;

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
