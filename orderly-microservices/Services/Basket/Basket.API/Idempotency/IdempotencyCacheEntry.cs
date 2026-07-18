namespace Basket.API.Idempotency;

/// <summary>
/// Cached payload stored in Redis at
/// <c>basket:idem:{userId}:{restaurantId}:{idempotencyKey}</c> when a
/// <c>POST /api/v1/cart/checkout</c> request with a fresh
/// <c>Idempotency-Key</c> header completes. JSON-serialised.
/// </summary>
/// <param name="StatusCode">
/// HTTP status code from the original handler's response (typically
/// <c>200</c> on success, <c>409</c> on empty basket, etc.). The
/// filter re-emits this verbatim on a matching replay.
/// </param>
/// <param name="Body">
/// Raw response body bytes (UTF-8 JSON). Replayed verbatim — the
/// filter does NOT re-deserialise and re-serialise, which would risk
/// drift if the contract evolves between the original write and a
/// long-delayed replay.
/// </param>
/// <param name="ContentType">
/// Response <c>Content-Type</c> header from the original. The filter
/// re-emits this verbatim.
/// </param>
/// <param name="BodyHash">
/// Hex-encoded SHA-256 of the original request body. The filter
/// compares this against the current request's hash to decide
/// "same body → replay" vs "different body → 422". Pre-computed
/// once at cache-write time so a replay doesn't have to re-hash the
/// stored body bytes (which would only re-confirm what we already
/// hashed).
/// </param>
/// <param name="StoredAt">
/// NodaTime <see cref="Instant"/> when the entry was written. Useful
/// for operator debugging ("when did this replay land?") — not
/// used by the filter logic.
/// </param>
public record IdempotencyCacheEntry(
    int StatusCode,
    byte[] Body,
    string ContentType,
    string BodyHash,
    Instant StoredAt);
