using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Dev;

/// <summary>
/// Centralises the IsDevelopment() + JWT_SECRET matrix that the dev
/// HS256 fallback depends on. Lives in one place so the production
/// guard in <see cref="DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback"/>
/// and any future code path (integration tests, host-level middleware
/// filters) read the exact same rule.
/// </summary>
/// <remarks>
/// <para>The matrix:</para>
/// <list type="table">
/// <listheader><term>Environment</term><description><c>JWT_SECRET</c></description></listheader>
/// <item><term>Development</term><description>set → register HS256 fallback (today's behaviour)</description></item>
/// <item><term>Development</term><description>unset → silently no-op (today's behaviour)</description></item>
/// <item><term>Staging / Production</term><description>set → throw <see cref="ProductionJwtKeyLoadException"/></description></item>
/// <item><term>Staging / Production</term><description>unset → register OpenIddict-only path (today's behaviour)</description></item>
/// </list>
/// <para>The fallback path registers a <c>SymmetricSecurityKey</c>-backed
/// scheme; leaking the secret outside Development lets a caller forge
/// admin tokens. Hence the fail-closed check in the extension itself
/// and the parallel condition testable via
/// <see cref="IsDevJwtAllowed"/>.</para>
/// </remarks>
public static class DevJwtEnvironment
{
    /// <summary>
    /// Config key read from <see cref="IConfiguration"/>. In
    /// ASP.NET Core's default configuration providers this picks up
    /// the env var of the same name automatically — the same env var
    /// the MCP server reads when signing tokens.
    /// </summary>
    public const string JwtSecretConfigKey = DevJwtBearerFallbackExtensions.JwtSecretEnvVar;

    /// <summary>
    /// True iff the dev HS256 fallback should register its scheme.
    /// Both conditions must hold simultaneously.
    /// </summary>
    /// <param name="env">The host environment.</param>
    /// <param name="config">The application's configuration. The caller is responsible for passing the configuration that backs the running host so secret values match.</param>
    public static bool IsDevJwtAllowed(IWebHostEnvironment env, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(config);

        return env.IsDevelopment()
            && !string.IsNullOrEmpty(config[JwtSecretConfigKey]);
    }

    /// <summary>
    /// True iff a leaked <c>JWT_SECRET</c> would be loadable in the
    /// current environment. The extension uses this as the trigger for
    /// <see cref="ProductionJwtKeyLoadException"/>; integration tests
    /// use it to assert the matrix above without spinning up the full
    /// AuthenticationBuilder.
    /// </summary>
    public static bool IsProductionWithLeakedJwtSecret(IWebHostEnvironment env, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(config);

        return !env.IsDevelopment()
            && !string.IsNullOrEmpty(config[JwtSecretConfigKey]);
    }
}
