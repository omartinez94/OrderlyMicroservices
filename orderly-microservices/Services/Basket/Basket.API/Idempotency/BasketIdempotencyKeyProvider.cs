using System.Security.Cryptography;
using System.Text;

namespace Basket.API.Idempotency;

/// <summary>
/// Default <see cref="IBasketIdempotencyKeyProvider"/> implementation.
/// Reads the server-side secret from <see cref="BasketIdempotencyOptions"/>
/// at construction; the dev-only fallback logs and synthesizes a
/// per-process random key when running under
/// <see cref="IHostEnvironment.IsDevelopment"/> and the config value
/// is empty.
/// </summary>
public sealed class BasketIdempotencyKeyProvider : IBasketIdempotencyKeyProvider
{
    private const int MinKeyBytes = 16;

    private readonly byte[] _key;
    private readonly ILogger<BasketIdempotencyKeyProvider> _logger;

    public BasketIdempotencyKeyProvider(
        IOptions<BasketIdempotencyOptions> options,
        ILogger<BasketIdempotencyKeyProvider> logger,
        IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(env);
        _logger = logger;

        var raw = options.Value.SecretHex;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (env.IsDevelopment())
            {
                _key = RandomNumberGenerator.GetBytes(32);
                _logger.LogWarning(
                    "Basket:Idempotency:SecretHex not configured; using a per-process random key. " +
                    "Idempotency cache entries are valid only for this process lifetime. " +
                    "For persistent idempotency across restarts, set the value via " +
                    "`dotnet user-secrets set Basket:Idempotency:SecretHex <32-byte-hex>`.");
                return;
            }

            throw new InvalidOperationException(
                "Basket:Idempotency:SecretHex missing from configuration. " +
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
                "Basket:Idempotency:SecretHex must be a hex string (e.g., 32 random bytes → 64 hex chars).",
                ex);
        }

        if (_key.Length < MinKeyBytes)
        {
            throw new InvalidOperationException(
                $"Basket:Idempotency:SecretHex must decode to at least {MinKeyBytes} bytes (got {_key.Length}).");
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
