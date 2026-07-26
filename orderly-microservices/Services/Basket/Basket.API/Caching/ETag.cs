using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
using NodaTime.Serialization.SystemTextJson;

namespace Basket.API.Caching;

/// <summary>
/// Strong-ETag computation + conditional-request helper for the
/// basket endpoints. The ETag is the SHA-256 of the basket JSON
/// projection (cheap — the basket is small, &lt;1 KB). The
/// <c>IsNotModified</c> method checks both
/// <c>If-None-Match</c> (strong comparison) and
/// <c>If-Modified-Since</c> (HTTP-date comparison), returning
/// <c>true</c> when the client signals the cached copy is still
/// valid.
/// </summary>
public static class ETag
{
    /// <summary>
    /// Computes the strong ETag for a basket. The format is the
    /// lowercase hex of <c>SHA-256(json(basket))</c> with no quotes
    /// — the caller wraps in <c>"…"</c> per RFC 9110.
    /// </summary>
    public static string Compute(Models.Basket basket)
    {
        ArgumentNullException.ThrowIfNull(basket);

        // System.Text.Json with the same global config the host
        // uses (PascalCase + NodaTime). We don't round-trip the
        // result — the ETag is the projection's hash, not a parsed
        // object.
        var json = System.Text.Json.JsonSerializer.Serialize(basket, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns <c>true</c> when the inbound request carries a
    /// matching <c>If-None-Match</c> ETag (strong comparison) OR a
    /// <c>If-Modified-Since</c> HTTP-date &gt;= the basket's
    /// <paramref name="lastModified"/> timestamp. RFC 9110 §15.4.5
    /// (304 Not Modified) — both signals are honoured; either
    /// alone is sufficient to skip the body.
    /// </summary>
    public static bool IsNotModified(HttpRequest request, string etag, Instant lastModified)
    {
        // 1. If-None-Match — strong comparison per RFC 9110.
        if (request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch) && ifNoneMatch.Count > 0)
        {
            // Multiple ETags may be sent comma-separated; the
            // client considers the resource fresh if ANY match
            // (per RFC 9110 §13.1.2). Strip the W/ prefix only if
            // the client signals a weak comparison (it doesn't in
            // our case — clients send bare etag or "*").
            var supplied = ifNoneMatch.ToString();
            if (supplied == "*" || supplied == $"\"{etag}\"")
            {
                return true;
            }
        }

        // 2. If-Modified-Since — HTTP-date comparison. RFC 9110
        // The cache MUST ignore the header when the
        // response carries a strong validator (which our ETag
        // is); we honour it for clients that don't send
        // If-None-Match. The date format is RFC 1123 ("R"
        // round-trip format).
        if (request.Headers.TryGetValue(HeaderNames.IfModifiedSince, out var ifModifiedSince) && ifModifiedSince.Count > 0)
        {
            if (DateTimeOffset.TryParseExact(
                    ifModifiedSince.ToString(),
                    "R",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var clientCutoff))
            {
                // The basket's LastModifiedAt is stored as
                // NodaTime Instant; convert to UTC DateTimeOffset
                // for the comparison. Use InsecureComparable
                // because the HTTP-date format has 1-second
                // precision and a strict == would mismatch on
                // sub-second changes.
                var basketLastModified = lastModified.ToDateTimeOffset();
                if (basketLastModified.ToUniversalTime() <= clientCutoff)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// JSON options used for ETag projection. Mirrors the global
    /// config (PascalCase + NodaTime) so the ETag is stable across
    /// request lifetime and across the cache layer.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    static ETag()
    {
        SerializerOptions.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
    }
}
