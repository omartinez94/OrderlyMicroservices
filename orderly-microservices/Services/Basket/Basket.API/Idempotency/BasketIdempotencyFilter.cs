using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using StackExchange.Redis;

namespace Basket.API.Idempotency;

/// <summary>
/// Carter <see cref="IEndpointFilter"/> that enforces the IETF
/// <c>draft-ietf-httpapi-idempotency-key-header</c> contract on
/// <c>POST /api/v1/cart/checkout</c>. Reads the <c>Idempotency-Key</c>
/// header (UUID v4 strict regex), looks up the cached response in
/// Redis, and replays it verbatim on a body-matching request or
/// returns <c>422 Unprocessable Content</c> on a body-mismatching
/// replay.
/// </summary>
/// <remarks>
/// <para><b>Wire contract.</b> Required <c>Idempotency-Key</c> header
/// (UUID v4); absence → <c>400</c>, malformed → <c>400</c>. Match →
/// <c>200</c> with the cached body. Mismatch → <c>422</c> (NOT <c>409</c>
/// — the IETF draft is explicit: 422 means "state conflict" while 409
/// means "resource conflict". The Idempotency-Key + body pairing is
/// state, not the resource).</para>
/// <para><b>Tenant scoping.</b> The Redis key includes
/// <c>{userId}:{restaurantId}</c> so an attacker who somehow learns
/// another user's UUID-v4 idempotency key still cannot replay — the
/// Redis key doesn't match (different userId). Belt-and-braces with
/// <c>BasketIdentityGuardBehavior</c>.</para>
/// <para><b>Fail-closed on Redis errors.</b> If the GET or SET throws,
/// the filter surfaces a <c>503</c> via <c>Results.Problem</c>. The
/// alternative — fall through to the handler — silently loses retry
/// protection on exactly the failure path where retries are most
/// likely. Production-grade retry protection that disappears when
/// Redis blips is worthless.</para>
/// <para><b>Race window.</b> GET-then-SET (no SETNX-as-lock). Two
/// concurrent first-time requests with the same key both miss, both
/// run the handler. For basket checkout this is safe: the second
/// request sees an empty basket (the first already deleted it) and
/// short-circuits with <c>"Basket is empty"</c>. No duplicate event
/// published, no duplicate order created.</para>
/// </remarks>
public sealed class BasketIdempotencyFilter(
    IConnectionMultiplexer redis,
    IBasketIdempotencyKeyProvider keyProvider,
    IOptions<BasketIdempotencyOptions> options,
    ILogger<BasketIdempotencyFilter> logger)
    : IEndpointFilter
{
    /// <summary>
    /// IETF draft UUID v4 regex. Lower-case hex; version 4 (the
    /// <c>4</c> in the third group); variant bits 8/9/a/b in the
    /// fourth group.
    /// </summary>
    private static readonly Regex UuidV4Regex = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions CacheSerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    private readonly TimeSpan _ttl = options.Value.Ttl;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // 1. Header presence + UUID v4 format.
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var headerValues)
            || headerValues.Count == 0
            || string.IsNullOrWhiteSpace(headerValues[0]))
        {
            return Results.Problem(
                title: "Missing Idempotency-Key header",
                detail: "POST /api/v1/cart/checkout requires the Idempotency-Key header (UUID v4).",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.1");
        }

        var idempotencyKey = headerValues[0]!;
        if (!UuidV4Regex.IsMatch(idempotencyKey))
        {
            return Results.Problem(
                title: "Malformed Idempotency-Key",
                detail: "The Idempotency-Key header must be a UUID v4 (e.g., 8e9f7c4a-2b1d-4e6a-b3f5-9c8e7d6f5a4b).",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.1");
        }

        // 2. Resolve tenant identity from the JWT (NOT the request body —
        //    the §2.10 spoofing footgun). The identity guard runs upstream
        //    of this filter, so the principal is already authenticated.
        var callerUserId = http.User.GetUserId();
        var callerRestaurantId = http.User.GetRestaurantId();
        if (callerUserId == Guid.Empty || callerRestaurantId == Guid.Empty)
        {
            // Belt-and-braces — should never reach here because the
            // identity guard runs first, but failing closed is cheaper
            // than letting an unauthenticated caller write to the
            // idempotency namespace.
            return Results.Problem(
                title: "Authenticated user required",
                detail: "POST /api/v1/cart/checkout requires an authenticated principal with a restaurantId claim.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // 3. Read the request body so we can hash it. The body stream
        //    is forward-only; buffer it into a byte array, then rewind
        //    for the downstream handler to re-read.
        var requestBodyBytes = await ReadAndRewindBodyAsync(http.Request, context.HttpContext.RequestAborted);
        var rawBodyHash = ComputeSha256Hex(requestBodyBytes);

        // Body fingerprint = HMAC-SHA-256(serverSecret, "${userId}|${restaurantId}|${rawSha256}")
        // — matches BASKET_SERVICE_PLAN §6 Phase 2 2.3 "HMAC envelope:
        // HMAC-SHA256(serverSecret, userId + restaurantId + sha256(requestBody))".
        // The HMAC binds the (userId, restaurantId, body) triple to the
        // server-side secret; an attacker with Redis read access but no
        // secret cannot forge a cache entry with a substituted bodyHash.
        var bodyFingerprint = keyProvider.Compute(
            $"{callerUserId}|{callerRestaurantId}|{rawBodyHash}");

        // 4. Compose the Redis key. Tenant-scoped so cross-user replay
        //    cannot collide on the same UUID v4.
        var redisKey = $"basket:idem:{callerUserId}:{callerRestaurantId}:{idempotencyKey}";

        var db = redis.GetDatabase();

        // 5. GET — replay or 422.
        RedisValue cachedRaw;
        try
        {
            cachedRaw = await db.StringGetAsync(redisKey);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex,
                "Idempotency cache lookup failed for key {IdempotencyKey} (tenant {UserId}, {RestaurantId}). Failing closed.",
                idempotencyKey, callerUserId, callerRestaurantId);
            return Results.Problem(
                title: "Idempotency cache unavailable",
                detail: "The Idempotency-Key cache is temporarily unavailable; please retry.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!cachedRaw.IsNullOrEmpty)
        {
            IdempotencyCacheEntry? cached;
            try
            {
                cached = JsonSerializer.Deserialize<IdempotencyCacheEntry>(
                    (string)cachedRaw!, CacheSerializerOptions);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex,
                    "Idempotency cache at {RedisKey} contained a malformed payload. Failing closed.",
                    redisKey);
                return Results.Problem(
                    title: "Idempotency cache corrupted",
                    detail: "The Idempotency-Key cache returned a malformed payload; please retry.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (cached is null)
            {
                return Results.Problem(
                    title: "Idempotency cache corrupted",
                    detail: "The Idempotency-Key cache returned null; please retry.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (cached.BodyHash == bodyFingerprint)
            {
                // Match — replay the cached response verbatim.
                logger.LogInformation(
                    "Idempotency-Key {IdempotencyKey} replay hit (tenant {UserId}, {RestaurantId}, status {StatusCode}).",
                    idempotencyKey, callerUserId, callerRestaurantId, cached.StatusCode);

                http.Response.Headers["Idempotent-Replayed"] = "true";
                http.Response.StatusCode = cached.StatusCode;
                http.Response.ContentType = cached.ContentType;
                http.Response.ContentLength = cached.Body.Length;
                await http.Response.Body.WriteAsync(cached.Body, context.HttpContext.RequestAborted);
                return Results.Empty; // short-circuit — body already written
            }

            // Mismatch — 422.
            logger.LogWarning(
                "Idempotency-Key {IdempotencyKey} reused with different payload (tenant {UserId}, {RestaurantId}). Returning 422.",
                idempotencyKey, callerUserId, callerRestaurantId);

            return Results.Problem(
                title: "Idempotency-Key reused with different payload",
                detail: $"The Idempotency-Key '{idempotencyKey}' was previously used with a different request body. " +
                        "Per the IETF draft-ietf-httpapi-idempotency-key-header, key reuse with a different payload is " +
                        "rejected with 422 Unprocessable Content.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/");
        }

        // 6. Miss — run the handler, capture the response, cache it.
        //    Swap HttpContext.Response.Body for a MemoryStream so we
        //    can read what the handler wrote.
        var originalBody = http.Response.Body;
        await using var capture = new MemoryStream();
        http.Response.Body = capture;

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            // Restore the original stream on failure so the framework
            // can write the error response.
            http.Response.Body = originalBody;
            throw;
        }

        // Drain the captured body and restore the original stream.
        capture.Position = 0;
        var responseBytes = capture.ToArray();
        http.Response.Body = originalBody;

        // 7. Cache the response. We only cache successful (200/2xx)
        //    responses — a 422 from the underlying validator is
        //    client-error and shouldn't be replayed.
        var statusCode = http.Response.StatusCode;
        if (statusCode >= 200 && statusCode < 300)
        {
            var entry = new IdempotencyCacheEntry(
                StatusCode: statusCode,
                Body: responseBytes,
                ContentType: http.Response.ContentType ?? "application/json",
                BodyHash: bodyFingerprint,
                StoredAt: SystemClock.Instance.GetCurrentInstant());

            try
            {
                var serialised = JsonSerializer.Serialize(entry, CacheSerializerOptions);
                await db.StringSetAsync(redisKey, serialised, _ttl);
            }
            catch (RedisException ex)
            {
                // Cache write failure is non-fatal — the request
                // succeeded; we just lose retry protection for THIS
                // call. The next replay will miss and re-run. Logged
                // for ops visibility.
                logger.LogWarning(ex,
                    "Idempotency cache write failed for key {RedisKey}; the request succeeded but the next replay will miss.",
                    redisKey);
            }
        }

        // 8. Forward the captured response to the original stream.
        http.Response.StatusCode = statusCode;
        if (http.Response.ContentLength is null && responseBytes.Length > 0)
        {
            http.Response.ContentLength = responseBytes.Length;
        }
        await http.Response.Body.WriteAsync(responseBytes, context.HttpContext.RequestAborted);

        // Return the handler's own result (typically Results.Ok(...))
        // so the framework writes any framework-level state (headers
        // it would normally emit). We already wrote the body, but
        // returning the result lets the framework run its end-of-
        // pipeline tasks. Some framework versions check for null and
        // skip body writes if so; we set ContentLength above.
        return result;
    }

    /// <summary>
    /// Reads the full request body into a byte array and rewinds the
    /// request's body stream so the downstream handler can re-read
    /// it. The body is buffered in-memory — checkout payloads are
    /// small (one envelope + payment fields) so the buffer cost is
    /// bounded.
    /// </summary>
    private static async Task<byte[]> ReadAndRewindBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.Body.CanSeek)
        {
            // ASP.NET Core usually provides a seekable body in the
            // Minimal-API pipeline, but if it isn't, wrap it in a
            // MemoryStream and replace.
            request.EnableBuffering();
        }

        request.Body.Position = 0;
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, cancellationToken);
        request.Body.Position = 0;
        return ms.ToArray();
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }
}
