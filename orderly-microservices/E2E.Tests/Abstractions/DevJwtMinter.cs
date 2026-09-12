using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace E2E.Tests.Abstractions;

/// <summary>
/// Mints HS256-signed JWTs that the running services accept via the
/// <c>BuildingBlocks.Dev.DevJwtBearerFallbackExtensions</c> HS256
/// fallback. The MCP server uses the same key/issuer/audience shape —
/// see <c>BuildingBlocks.Dev.DevJwtBearerFallbackExtensions.DevIssuer</c>
/// and <c>DevAudience</c> constants — so E2E and MCP-issued tokens
/// are interchangeable.
/// </summary>
/// <remarks>
/// <para>The signing key is read once at construction from
/// <paramref name="jwtSecret"/> — the same value the services load
/// from the <c>JWT_SECRET</c> environment variable when the dev
/// fallback registers. The runtime posture is "Development +
/// JWT_SECRET" — anything else and the host refuses to start (see
/// <c>ProductionJwtKeyLoadException</c>).</para>
/// <para>Each token includes the <c>restaurantId</c> claim the
/// tenant-aware services bind on (Tenant == Restaurant, JWT carrier),
/// plus one permission per line of <paramref name="permissions"/>.
/// The <c>sub</c> claim is the test's <paramref name="userId"/>;
/// identity is the literal <c>"orderly-devmcp"</c> string the fallback
/// expects.</para></remarks>
public sealed class DevJwtMinter(string jwtSecret, string issuer = "orderly-devmcp", string audience = "OrderlyMicroservices")
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(jwtSecret));

    /// <summary>
    /// Mints a token whose <c>restaurantId</c> claim matches
    /// <paramref name="restaurantId"/>, whose <c>sub</c> is
    /// <paramref name="userId"/>, and whose <c>permission</c> claims
    /// cover every permission in <paramref name="permissions"/>.
    /// Token TTL: 1 hour.
    /// </summary>
    public string Mint(Guid userId, Guid restaurantId, params string[] permissions)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("restaurantId", restaurantId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (string permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
