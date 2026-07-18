using System.ComponentModel.DataAnnotations;

namespace Basket.API.Idempotency;

/// <summary>
/// Strongly-typed configuration for the <c>POST /api/v1/cart/checkout</c>
/// idempotency filter. Bound from the <c>Basket:Idempotency</c>
/// configuration section via <c>AddOptions&lt;&gt;().Bind(...).ValidateDataAnnotations().ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// A bad config fails the host boot at <c>startAsync</c>, not at first
/// request (per plan §0.3.5). The dev-only random key fallback lives in
/// <see cref="BasketIdempotencyKeyProvider"/>; <see cref="SecretHex"/>
/// here is the production value (32 hex chars = 16 bytes minimum).
/// </remarks>
public sealed class BasketIdempotencyOptions
{
    public const string SectionName = "Basket:Idempotency";

    /// <summary>
    /// Server-side HMAC secret. Hex-encoded; minimum 16 bytes (32 hex
    /// chars). Production: 32 random bytes = 64 hex chars. NEVER log
    /// this value — the filter's HMAC envelope uses it as a salt, and
    /// leaking the secret lets an attacker forge replay entries that
    /// collide on legitimate keys (they'd need both the key and the
    /// secret).
    /// </summary>
    [Required]
    [RegularExpression("^[0-9a-fA-F]{32,}$",
        ErrorMessage = "SecretHex must be a hex string of at least 32 characters (16 bytes).")]
    public string SecretHex { get; set; } = default!;

    /// <summary>
    /// Redis key TTL for cached replay entries. Default 24 hours
    /// matches the IETF draft recommendation. Operators can bump this
    /// to widen the retry window without code changes.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00",
        ErrorMessage = "Ttl must be between 1 minute and 7 days.")]
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);
}
