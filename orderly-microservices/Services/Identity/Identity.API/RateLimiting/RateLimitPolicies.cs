using System.Net;

namespace Identity.API.RateLimiting;

/// <summary>
/// Named rate-limit policies and the partition-key strategy shared by them.
/// </summary>
/// <remarks>
/// Identity runs behind the YARP gateway, which makes the naive partition key
/// — <c>HttpContext.Connection.RemoteIpAddress</c> — actively harmful: every
/// proxied request presents the *gateway's* address, so an IP-partitioned
/// limiter funnels the entire user base into one shared bucket. The first few
/// callers in each window consume it and everyone else is locked out.
/// <see cref="ClientPartitionKey"/> resolves the real caller instead.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>
    /// Strict policy for endpoints that accept credentials (login, register,
    /// token). This is the brute-force defence; it is deliberately far tighter
    /// than the global fallback and is applied per-endpoint via
    /// <c>RequireRateLimiting</c>.
    /// </summary>
    public const string Auth = "auth";

    /// <summary>
    /// Resolves the partition key for a request: the originating client's IP
    /// where it can be established, otherwise the direct peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>X-Forwarded-For</c> is honoured only when the immediate peer is on a
    /// private network — i.e. plausibly our own gateway. Trusting the header
    /// unconditionally would be worse than ignoring it: any caller could mint
    /// a fresh partition key per request and bypass the limiter completely.
    /// </para>
    /// <para>
    /// The <b>right-most</b> entry is used, not the left-most. XFF is built by
    /// appending, so a client that sends its own <c>X-Forwarded-For: 1.2.3.4</c>
    /// produces <c>1.2.3.4, &lt;real client&gt;</c> at the gateway — the
    /// left-most entry is attacker-controlled, while the right-most is the
    /// address our own proxy actually observed.
    /// </para>
    /// </remarks>
    public static string ClientPartitionKey(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress;

        if (peer is not null && IsTrustedProxy(peer))
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"];

            // Enumerate in reverse: the last hop appended by our own proxy.
            for (var i = forwardedFor.Count - 1; i >= 0; i--)
            {
                var header = forwardedFor[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var lastComma = header.LastIndexOf(',');
                var candidate = lastComma >= 0
                    ? header.AsSpan(lastComma + 1)
                    : header.AsSpan();

                if (TryParseAddress(candidate.Trim(), out var client))
                {
                    return client.ToString();
                }
            }
        }

        return peer?.ToString() ?? "unknown";
    }

    /// <summary>
    /// True when the address belongs to a range our own infrastructure runs
    /// on (loopback, RFC1918, IPv6 unique-local) and its forwarding headers
    /// may therefore be believed.
    /// </summary>
    private static bool IsTrustedProxy(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        switch (address.AddressFamily)
        {
            case System.Net.Sockets.AddressFamily.InterNetwork:
                {
                    Span<byte> octets = stackalloc byte[4];
                    if (!address.TryWriteBytes(octets, out _))
                    {
                        return false;
                    }

                    // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 — covers the
                    // default Docker bridge and any private cluster network.
                    return octets[0] switch
                    {
                        10 => true,
                        172 => octets[1] >= 16 && octets[1] <= 31,
                        192 => octets[1] == 168,
                        _ => false,
                    };
                }

            case System.Net.Sockets.AddressFamily.InterNetworkV6:
                {
                    Span<byte> bytes = stackalloc byte[16];
                    if (!address.TryWriteBytes(bytes, out _))
                    {
                        return false;
                    }

                    // fc00::/7 — IPv6 unique-local.
                    return (bytes[0] & 0xFE) == 0xFC;
                }

            default:
                return false;
        }
    }

    private static bool TryParseAddress(ReadOnlySpan<char> candidate, out IPAddress address)
    {
        // Strip an optional port. Bare IPv6 contains colons, so only treat a
        // trailing ":nnnn" as a port when it is unambiguous: either the value
        // is bracketed ("[::1]:443") or it is IPv4-shaped with one colon.
        if (candidate.Length > 0 && candidate[0] == '[')
        {
            var close = candidate.IndexOf(']');
            if (close > 0)
            {
                candidate = candidate[1..close];
            }
        }
        else
        {
            var colon = candidate.IndexOf(':');
            if (colon >= 0 && candidate[(colon + 1)..].IndexOf(':') < 0)
            {
                candidate = candidate[..colon];
            }
        }

        return IPAddress.TryParse(candidate, out address!);
    }
}
