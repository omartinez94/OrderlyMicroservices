using System.Security.Cryptography;

namespace BuildingBlocks.Identifiers;

/// <summary>
/// Thread-safe generator of compact, typed short identifiers (e.g. rstk8j3x9mq).
/// Employs an unambiguous alphabet excluding visually confusable characters (0, O, 1, l, I).
/// Produces contiguous single-token identifiers without underscores or punctuation.
/// </summary>
public static class ShortIdGenerator
{
    /// <summary>
    /// Unambiguous 31-character alphabet: numbers 2-9 and lowercase letters excluding i, l, o.
    /// Excludes: 0, O, 1, l, I, i, o.
    /// </summary>
    public const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    public const int DefaultRandomLength = 8;
    public const int MinRandomLength = 8;
    public const int MaxRandomLength = 10;

    /// <summary>
    /// Generates a contiguous typed short identifier for the specified <see cref="EntityPrefix"/>.
    /// Format: [prefix][8 to 10 random chars] (e.g. rstk8j3x9mq).
    /// </summary>
    /// <param name="prefix">The entity type prefix.</param>
    /// <param name="randomLength">Length of the random token suffix (defaults to 8, allowed 8-10).</param>
    /// <returns>A contiguous, typed short identifier string.</returns>
    public static string Generate(EntityPrefix prefix, int randomLength = DefaultRandomLength)
    {
        return Generate(prefix.ToPrefixString(), randomLength);
    }

    /// <summary>
    /// Generates a contiguous typed short identifier with the given string prefix.
    /// Format: [prefix][8 to 10 random chars] (e.g. rstk8j3x9mq).
    /// </summary>
    /// <param name="prefix">The prefix string (e.g. "rst").</param>
    /// <param name="randomLength">Length of the random token suffix (defaults to 8, allowed 8-10).</param>
    /// <returns>A contiguous, typed short identifier string.</returns>
    public static string Generate(string prefix, int randomLength = DefaultRandomLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (randomLength < MinRandomLength || randomLength > MaxRandomLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(randomLength),
                randomLength,
                $"Random suffix length must be between {MinRandomLength} and {MaxRandomLength} characters.");
        }

        string randomSuffix = RandomNumberGenerator.GetString(Alphabet, randomLength);
        return $"{prefix.ToLowerInvariant()}{randomSuffix}";
    }

    /// <summary>
    /// Checks if a string conforms to the expected format and unambiguous alphabet for short IDs.
    /// </summary>
    /// <param name="shortId">The short ID candidate to validate.</param>
    /// <param name="expectedPrefix">Optional specific entity prefix to enforce.</param>
    /// <returns>True if valid; otherwise false.</returns>
    public static bool IsValid(string? shortId, EntityPrefix? expectedPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(shortId))
        {
            return false;
        }

        string expected = expectedPrefix.HasValue ? expectedPrefix.Value.ToPrefixString() : string.Empty;
        if (!string.IsNullOrEmpty(expected))
        {
            if (!shortId.StartsWith(expected, StringComparison.Ordinal))
            {
                return false;
            }

            int suffixLength = shortId.Length - expected.Length;
            if (suffixLength < MinRandomLength || suffixLength > MaxRandomLength)
            {
                return false;
            }

            ReadOnlySpan<char> suffix = shortId.AsSpan(expected.Length);
            foreach (char c in suffix)
            {
                if (!Alphabet.Contains(c))
                {
                    return false;
                }
            }

            return true;
        }

        // Generic validation when no specific prefix is expected:
        // Must be at least 3 chars (prefix) + 8 chars (suffix) = 11, up to e.g. 5 + 10 = 15 chars.
        if (shortId.Length < 3 + MinRandomLength || shortId.Length > 16)
        {
            return false;
        }

        // Suffix must be entirely within the alphabet (assume at least first 3 chars are prefix)
        ReadOnlySpan<char> genericSuffix = shortId.AsSpan(3);
        foreach (char c in genericSuffix)
        {
            if (!Alphabet.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to extract known entity prefix from a candidate short ID string.
    /// </summary>
    public static bool TryExtractPrefix(string? shortId, out EntityPrefix prefix)
    {
        prefix = default;
        if (string.IsNullOrWhiteSpace(shortId) || shortId.Length < 3)
        {
            return false;
        }

        string candidatePrefix = shortId[..3];
        switch (candidatePrefix)
        {
            case EntityPrefixConstants.Restaurant:
                prefix = EntityPrefix.Restaurant;
                return true;
            case EntityPrefixConstants.Order:
                prefix = EntityPrefix.Order;
                return true;
            case EntityPrefixConstants.Brand:
                prefix = EntityPrefix.Brand;
                return true;
            case EntityPrefixConstants.Table:
                prefix = EntityPrefix.Table;
                return true;
            case EntityPrefixConstants.MenuItem:
                prefix = EntityPrefix.MenuItem;
                return true;
            case EntityPrefixConstants.MenuCategory:
                prefix = EntityPrefix.MenuCategory;
                return true;
            case EntityPrefixConstants.Reservation:
                prefix = EntityPrefix.Reservation;
                return true;
            case EntityPrefixConstants.WalkInQueue:
                prefix = EntityPrefix.WalkInQueue;
                return true;
            case EntityPrefixConstants.User:
                prefix = EntityPrefix.User;
                return true;
            case EntityPrefixConstants.KitchenStation:
                prefix = EntityPrefix.KitchenStation;
                return true;
            default:
                return false;
        }
    }
}
