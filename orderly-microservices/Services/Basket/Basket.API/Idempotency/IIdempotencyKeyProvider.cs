namespace Basket.API.Idempotency;

/// <summary>
/// Server-side HMAC key for the <c>Idempotency-Key</c> filter on
/// <c>POST /api/v1/cart/checkout</c>. The cache key uses
/// <c>HMAC-SHA256(key, envelope)</c> keyed on a server-side secret,
/// NOT plain <c>SHA256(envelope)</c> — plain SHA-256 lets an attacker
/// craft a <c>userId + restaurantId + key</c> collision if they guess
/// the input format; HMAC requires knowledge of the secret.
/// </summary>
/// <remarks>
/// Mirrors <c>Discount.Grpc.Authorization.IIdempotencyKeyProvider</c>'s
/// shape (Discount ships the same pattern at
/// <c>Services/Discount/Discount.Grpc/Authorization/IdempotencyKeyProvider.cs</c>).
/// A future BuildingBlocks contribution could promote both providers
/// to <c>BuildingBlocks.Idempotency</c>; for Phase 2 v1 the duplication
/// is intentional (the secret is per-service — Basket's secret is in
/// <c>Basket:IdempotencyKey</c>, Discount's is in
/// <c>Discount:IdempotencyKey</c>, and sharing the secret would let
/// a Discount-cache-poisoning bug bleed into Basket's idempotency
/// namespace).
/// </remarks>
public interface IBasketIdempotencyKeyProvider
{
    /// <summary>
    /// Computes the cache key for an idempotency envelope. Returns an
    /// upper-case hex string of the HMAC-SHA256 MAC. Two envelopes
    /// that differ in any byte produce different MACs.
    /// </summary>
    /// <param name="envelope">
    /// Canonical envelope string (e.g.,
    /// <c>${userId}|${restaurantId}|${rawRequestBody}</c>). Must not
    /// be null or empty.
    /// </param>
    string Compute(string envelope);
}
