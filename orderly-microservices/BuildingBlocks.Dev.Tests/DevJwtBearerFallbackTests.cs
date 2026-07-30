using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Dev.Tests;

/// <summary>
/// Verifies the dev-only HS256 fallback scheme accepts tokens signed
/// with the same <c>JWT_SECRET</c> the MCP server uses, and rejects
/// tokens signed with any other secret. Mirrors the round-trip that
/// <c>Orderly.DevMCP.Server/src/tools/auth.ts</c> exercises.
/// </summary>
public class DevJwtBearerFallbackTests
{
    private const string Secret = "dev-only-shared-secret-at-least-16-chars";

    [Fact]
    public void Hs256TokenSignedWithJwtSecret_ValidatesAgainstSymmetricKey()
    {
        // Mirrors the MCP server's `jwt.sign({...}, getSecret('JWT_SECRET'), { algorithm: 'HS256', ... })`.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "dev-user-id"),
            new Claim("restaurantId", "00000000-0000-0000-0000-000000000001"),
        };

        var jwt = new JwtSecurityToken(
            issuer: DevJwtBearerFallbackExtensions.DevIssuer,
            audience: DevJwtBearerFallbackExtensions.DevAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = DevJwtBearerFallbackExtensions.DevIssuer,
            ValidateAudience = true,
            ValidAudience = DevJwtBearerFallbackExtensions.DevAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, validationParameters, out _);

        principal.Should().NotBeNull();
        // JwtSecurityTokenHandler maps the standard "sub" claim to
        // System.Security.Claims.ClaimTypes.NameIdentifier by default;
        // verify both shapes since either lookup is valid for the dev
        // token shape.
        var subClaim = principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                       ?? principal.FindFirst(JwtRegisteredClaimNames.Sub);
        subClaim.Should().NotBeNull();
        subClaim!.Value.Should().Be("dev-user-id");
        principal.FindFirst("restaurantId")!.Value.Should().Be("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void Hs256TokenSignedWithDifferentSecret_IsRejected()
    {
        // Different secret — what a misconfigured dev host looks like.
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("totally-different-secret-also-long-enough"));

        var creds = new SigningCredentials(otherKey, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: DevJwtBearerFallbackExtensions.DevIssuer,
            audience: DevJwtBearerFallbackExtensions.DevAudience,
            claims: Array.Empty<Claim>(),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var expectedKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = DevJwtBearerFallbackExtensions.DevIssuer,
            ValidateAudience = true,
            ValidAudience = DevJwtBearerFallbackExtensions.DevAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = expectedKey,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);

        act.Should().Throw<SecurityTokenException>(
            "a token signed with a different secret must not validate against the expected key");
    }

    [Fact]
    public void ExpiredHs256Token_IsRejected()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: DevJwtBearerFallbackExtensions.DevIssuer,
            audience: DevJwtBearerFallbackExtensions.DevAudience,
            claims: Array.Empty<Claim>(),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = DevJwtBearerFallbackExtensions.DevIssuer,
            ValidateAudience = true,
            ValidAudience = DevJwtBearerFallbackExtensions.DevAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);
        act.Should().Throw<SecurityTokenExpiredException>();
    }
}