using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// <para><b>Environment gating:</b> the dev HS256 fallback registers
/// only when <see cref="DevJwtEnvironment.IsDevJwtAllowed"/> returns
/// true (Development environment + a non-empty <c>JWT_SECRET</c>).
/// When <c>JWT_SECRET</c> is set in a non-Development environment —
/// a leak that turns every dev token into a forgeable production
/// admin token — the extension throws
/// <see cref="ProductionJwtKeyLoadException"/> at registration time
/// so the host refuses to start rather than silently degrading.</para>
/// <para><b>Caller responsibility:</b> always pass the host's
/// <see cref="IWebHostEnvironment"/> and <see cref="IConfiguration"/>;
/// the production guard reads them to decide the registration shape.
/// The OpenIddict scheme is registered unconditionally; the HS256
/// fallback is the gated layer.</para>
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
    /// <param name="environment">
    /// The host's <see cref="IWebHostEnvironment"/>. Drives the
    /// dev-vs-production gate; <see cref="IHostEnvironment.IsDevelopment"/>
    /// returns <c>true</c> only when
    /// <c>ASPNETCORE_ENVIRONMENT=Development</c>.
    /// </param>
    /// <param name="configuration">
    /// The host's <see cref="IConfiguration"/>. The extension reads
    /// <c>JWT_SECRET</c> from this so a leaked env var cannot bypass
    /// the production guard; .NET's default configuration providers
    /// pick up env vars automatically, which keeps the
    /// <c>Orderly.DevMCP.Server</c> env-var contract intact.
    /// </param>
    /// <param name="authority">
    /// Production OpenIddict metadata URL. Same value as the
    /// non-fallback extension.
    /// </param>
    /// <param name="audience">
    /// Production audience. Same value as the non-fallback extension.
    /// </param>
    /// <exception cref="ProductionJwtKeyLoadException">
    /// Thrown when <c>JWT_SECRET</c> is set in a non-Development
    /// environment. The exception is the fail-closed signal: the host
    /// refuses to start rather than registering a forgeable HS256
    /// scheme.
    /// </exception>
    /// <remarks>
    /// Behaviour matrix:
    /// <list type="table">
    /// <listheader><term>Environment</term><description><c>JWT_SECRET</c></description><description>Result</description></listheader>
    /// <item><term>Development</term><description>set</description><description>OpenIddict + HS256 fallback (today)</description></item>
    /// <item><term>Development</term><description>unset</description><description>OpenIddict only (silent no-op, today)</description></item>
    /// <item><term>Staging / Production</term><description>set</description><description><see cref="ProductionJwtKeyLoadException"/> — host refuses to start</description></item>
    /// <item><term>Staging / Production</term><description>unset</description><description>OpenIddict only (today)</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddJwtAuthenticationWithDevFallback(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        string authority,
        string audience)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        // Production guard: fail closed if a leaked JWT_SECRET would
        // otherwise register a forgeable HS256 scheme. The check
        // belongs before any AddJwtBearer so the OpenIddict JWKS
        // scheme is also unreachable in that posture.
        if (DevJwtEnvironment.IsProductionWithLeakedJwtSecret(environment, configuration))
        {
            throw new ProductionJwtKeyLoadException(
                $"JWT_SECRET is set in environment '{environment.EnvironmentName}'; " +
                "the dev HS256 fallback is forbidden outside Development. " +
                "Unset the env var or run with ASPNETCORE_ENVIRONMENT=Development.");
        }

        var secret = configuration[JwtSecretEnvVar];

        // OpenIddict / production JWKS scheme — same shape as the
        // non-fallback extension's AddJwtBearer. Always registered.
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

        if (DevJwtEnvironment.IsDevJwtAllowed(environment, configuration))
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!)),
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