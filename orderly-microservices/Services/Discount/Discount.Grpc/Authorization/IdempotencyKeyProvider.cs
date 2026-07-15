using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Discount.Grpc.Authorization;

/// <summary>
/// Server-side HMAC key for the Idempotency-Key cache middleware
/// (<c>DiscountOptions:IdempotencyKey</c> in production). The middleware
/// phase that wires this lives in Phase 8; for Phase 1B the provider
/// ships registered as a singleton so the wiring commit is additive
/// without further BuildingBlocks churn.
/// </summary>
/// <remarks>
/// <para>The cache key uses
/// <c>HMAC-SHA256(key, envelope)</c> keyed on a server-side secret,
/// <em>not</em> plain <c>SHA256(envelope)</c>. Plain SHA256 lets an
/// attacker craft a <c>key+rId+code</c> collision if they guess the
/// input format; HMAC requires knowledge of the secret.</para>
/// <para>Per plan §0.4.1 v1.3 changelog O-L29, the provider has a
/// dev-only fallback: when <see cref="IHostEnvironment.IsDevelopment"/>
/// is true AND the config value is missing, the provider generates a
/// 32-byte random key at startup, logs a <c>WARN</c>, and registers
/// the random key. Production keeps the hard-fail behaviour (no
/// fallback — missing config in prod is a deployment error and must
/// surface loudly).</para>
/// </remarks>
public interface IIdempotencyKeyProvider
{
    /// <summary>
    /// Computes the cache key for an idempotency envelope. Returns an
    /// upper-case hex string of the HMAC-SHA256 MAC. Two envelopes
    /// that differ in any byte produce different MACs.
    /// </summary>
    /// <param name="envelope">
    /// Canonical envelope string (e.g.,
    /// <c>callerRestaurantId + endpoint + rawRequestBody</c>).
    /// Must not be null or empty.
    /// </param>
    string Compute(string envelope);
}

/// <summary>
/// Default <see cref="IIdempotencyKeyProvider"/> implementation. Reads
/// the server-side secret from <see cref="IConfiguration"/> at
/// construction; the dev-only fallback logs and synthesizes a per-process
/// random key when running under <see cref="IHostEnvironment.IsDevelopment"/>.
/// </summary>
public sealed class IdempotencyKeyProvider : IIdempotencyKeyProvider
{
    private const int MinKeyBytes = 16;

    private readonly byte[] _key;
    private readonly ILogger<IdempotencyKeyProvider> _logger;

    /// <summary>
    /// Production constructor. Reads
    /// <c>IConfiguration["Discount:IdempotencyKey"]</c> at startup.
    /// Hard-fails when the value is missing or shorter than
    /// <see cref="MinKeyBytes"/>.
    /// </summary>
    public IdempotencyKeyProvider(IConfiguration config, ILogger<IdempotencyKeyProvider> logger)
        : this(config, logger, isDevelopment: false)
    {
    }

    /// <summary>
    /// Dev-friendly constructor that picks the fallback path when
    /// <paramref name="isDevelopment"/> is true and the config value
    /// is missing. The test factory uses this overload to avoid the
    /// dev-only branch when running unit tests against the production
    /// constructor.
    /// </summary>
    public IdempotencyKeyProvider(
        IConfiguration config,
        ILogger<IdempotencyKeyProvider> logger,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var raw = config["Discount:IdempotencyKey"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (isDevelopment)
            {
                _key = RandomNumberGenerator.GetBytes(32);
                _logger.LogWarning(
                    "Discount:IdempotencyKey not configured; using a per-process random key. " +
                    "Idempotency cache entries are valid only for this process lifetime. " +
                    "For persistent idempotency across restarts, set the value via " +
                    "`dotnet user-secrets set Discount:IdempotencyKey <32-byte-hex>`.");
                return;
            }

            throw new InvalidOperationException(
                "Discount:IdempotencyKey missing from configuration. " +
                "Dev: appsettings.Development.json or `dotnet user-secrets`. " +
                "Prod: Key Vault.");
        }

        try
        {
            _key = Convert.FromHexString(raw);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Discount:IdempotencyKey must be a hex string (e.g., 32 random bytes → 64 hex chars).",
                ex);
        }

        if (_key.Length < MinKeyBytes)
        {
            throw new InvalidOperationException(
                $"Discount:IdempotencyKey must decode to at least {MinKeyBytes} bytes (got {_key.Length}).");
        }
    }

    /// <inheritdoc />
    public string Compute(string envelope)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelope);
        var bytes = Encoding.UTF8.GetBytes(envelope);
        var mac = HMACSHA256.HashData(_key, bytes);
        return Convert.ToHexString(mac);
    }
}
