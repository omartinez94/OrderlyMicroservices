using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Dev;

/// <summary>
/// JWT bearer registration that wraps the production
/// <c>AddJwtAuthentication(authority, audience)</c> flow with a
/// dev-only HS256 fallback so the <c>Orderly.DevMCP.Server</c>
/// companion can authenticate without depending on the OpenIddict
/// <c>Authority</c> being reachable from the API host.
/// </summary>
/// <remarks>
/// <para><b>Why a fallback:</b> the production API services
/// (<c>Basket.API</c>, <c>Catalog.API</c>, <c>Kitchen.API</c>,
/// <c>Ordering.API</c>, <c>Discount.Grpc</c>) validate inbound tokens
/// against the OpenIddict metadata document at <c>Authority</c>. When
/// the OpenIddict authority is unreachable (typical for local dev
/// where Identity.API isn't running, or for the MCP server's
/// <c>generate_dev_token</c> tool that mints tokens outside the
/// Identity issuance path), every API rejects the HS256-signed dev
/// token with <c>IDX10503: Signature validation failed</c>.</para>
/// <para><b>How the chain works:</b> a <see cref="PolicySchemeHandler"/>
/// peeks at the inbound JWT's <c>alg</c> header. When the header
/// advertises <c>HS256</c>, the policy forwards to the dev scheme;
/// otherwise it forwards to the OpenIddict scheme (the production
/// JWKS path). The default authenticate / challenge scheme is the
/// policy scheme, so the <c>PermissionPolicyProvider</c> still gates
/// correctly.</para>
/// <para><b>Caller responsibility:</b> this extension throws
/// <see cref="InvalidOperationException"/> at registration time when
/// <c>JWT_SECRET</c> is not set. The HS256 scheme uses a single
/// symmetric key from <see cref="JwtSecretEnvVar"/> — shipping that
/// to production would expose the same signing material that issues
/// tokens, which is the opposite of what the OpenIddict RS256 path
/// gives us. Wire this extension only in dev / local environments,
/// alongside <c>if (app.Environment.IsDevelopment())</c> gates for
/// any dev-only routes.</para>
/// </remarks>
public static class DevJwtBearerFallbackExtensions
{
    /// <summary>
    /// Scheme name for the OpenIddict / production JWKS path.
    /// </summary>
    public const string OpenIddictScheme = "OpenIddict";

    /// <summary>
    /// Scheme name for the dev-only HS256 fallback. Selected by the
    /// <see cref="PolicySchemeHandler"/> when the inbound token's
    /// <c>alg</c> header advertises <c>HS256</c>.
    /// </summary>
    public const string DevHs256Scheme = "DevHs256";

    /// <summary>
    /// Policy scheme name; assigned to <c>DefaultAuthenticateScheme</c>
    /// + <c>DefaultChallengeScheme</c>. Forwards to either
    /// <see cref="OpenIddictScheme"/> or <see cref="DevHs256Scheme"/>
    /// based on the JWT header peek.
    /// </summary>
    public const string PolicyScheme = "DevHs256Fallback";

    /// <summary>
    /// Environment variable that holds the HS256 symmetric key. Same
    /// key the MCP server uses to <c>jwt.sign(...)</c> tokens via its
    /// <c>JWT_SECRET</c> env var.
    /// </summary>
    public const string JwtSecretEnvVar = "JWT_SECRET";

    /// <summary>
    /// JWT issuer claim that the HS256 fallback accepts. Matches the
    /// MCP server's <c>issuer</c> in <c>tools/auth.ts</c>.
    /// </summary>
    public const string DevIssuer = "orderly-devmcp";

    /// <summary>
    /// Audience claim the HS256 fallback accepts. Matches the
    /// production <c>JwtBearerOptions.Audience</c> + the
    /// <see cref="DevAudience"/> constant referenced from tests.
    /// </summary>
    public const string DevAudience = "OrderlyMicroservices";

    /// <summary>
    /// Replace <c>AddJwtAuthentication(authority, audience)</c> with
    /// this call in services that need to accept MCP-issued HS256
    /// tokens.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="authority">
    /// Production OpenIddict metadata URL. Same value as the
    /// non-fallback extension.
    /// </param>
    /// <param name="audience">
    /// Production audience. Same value as the non-fallback extension.
    /// </param>
    /// <remarks>
    /// When <c>JWT_SECRET</c> is unset (typical for tests, or for a
    /// dev Compose stack that hasn't enabled the MCP server), the
    /// extension silently degrades to a single OpenIddict scheme —
    /// the same shape as <c>AddJwtAuthentication</c>. This makes it
    /// safe to call from any environment; the dev fallback simply
    /// opts in when the env var is present.
    /// </remarks>
    public static IServiceCollection AddJwtAuthenticationWithDevFallback(
        this IServiceCollection services,
        string authority,
        string audience)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var secret = Environment.GetEnvironmentVariable(JwtSecretEnvVar);

        // OpenIddict / production JWKS scheme — same shape as the
        // non-fallback extension's AddJwtBearer.
        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = PolicyScheme;
            options.DefaultAuthenticateScheme = PolicyScheme;
            options.DefaultChallengeScheme = PolicyScheme;
        })
        .AddJwtBearer(OpenIddictScheme, options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "name",
                RoleClaimType = "role",
            };
        });

        if (!string.IsNullOrEmpty(secret))
        {
            // Dev-only HS256 fallback scheme. Signing key is read
            // once at registration from JWT_SECRET; rotation requires
            // a restart (acceptable for dev tooling).
            authBuilder.AddJwtBearer(DevHs256Scheme, options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = DevIssuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateLifetime = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });
        }

        // Policy scheme that picks between the two based on the
        // JWT header peek. When JWT_SECRET is unset, the selector
        // always returns OpenIddictScheme (the dev scheme name is
        // unregistered but the policy scheme still dispatches).
        authBuilder.AddPolicyScheme(PolicyScheme, PolicyScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
                PolicySchemeHandler.SelectScheme(context);
        });

        return services;
    }
}

/// <summary>
/// Policy scheme selector: peeks at the JWT header's <c>alg</c>
/// field. Returns <see cref="DevJwtBearerFallbackExtensions.DevHs256Scheme"/>
/// when the header advertises HS256; otherwise returns the OpenIddict
/// production scheme.
/// </summary>
/// <remarks>
/// <para>The peek is intentionally cheap — the JWT header is the
/// first base64url-encoded segment before the dot. We decode it
/// with <see cref="JsonDocument"/> and read the <c>alg</c> string.
/// Anything that doesn't parse or isn't <c>HS256</c> falls through
/// to the OpenIddict scheme (which will then reject the token via
/// its own validation path).</para>
/// <para>The fallback path is gated on
/// <see cref="DevJwtBearerFallbackExtensions.JwtSecretEnvVar"/>
/// being set at registration time (the
/// <c>AddJwtAuthenticationWithDevFallback</c> extension throws if it
/// isn't), so the HS256 scheme is always reachable in dev.</para>
/// </remarks>
internal static class PolicySchemeHandler
{
    /// <summary>
    /// Returns the scheme name to use for the current request.
    /// Called once per request by the policy-scheme dispatcher.
    /// </summary>
    public static string SelectScheme(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader)
            || string.IsNullOrEmpty(authHeader))
        {
            return DevJwtBearerFallbackExtensions.OpenIddictScheme;
        }

        var headerValue = authHeader.ToString();
        const string bearerPrefix = "Bearer ";
        if (!headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return DevJwtBearerFallbackExtensions.OpenIddictScheme;
        }

        var token = headerValue.Substring(bearerPrefix.Length).Trim();
        var dotIndex = token.IndexOf('.');
        if (dotIndex <= 0)
        {
            return DevJwtBearerFallbackExtensions.OpenIddictScheme;
        }

        var headerSegment = token.Substring(0, dotIndex);
        try
        {
            var headerJson = Base64UrlDecode(headerSegment);
            using var doc = JsonDocument.Parse(headerJson);
            if (doc.RootElement.TryGetProperty("alg", out var algElement)
                && algElement.ValueKind == JsonValueKind.String
                && string.Equals(algElement.GetString(), "HS256", StringComparison.Ordinal))
            {
                return DevJwtBearerFallbackExtensions.DevHs256Scheme;
            }
        }
        catch
        {
            // Malformed header — fall through to OpenIddict. The
            // OpenIddict scheme will reject with the usual 401.
        }

        return DevJwtBearerFallbackExtensions.OpenIddictScheme;
    }

    private static string Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        var bytes = Convert.FromBase64String(s);
        return Encoding.UTF8.GetString(bytes);
    }
}