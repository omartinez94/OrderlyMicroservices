using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BuildingBlocks.Dev;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Catalog.API.Tests.Integration;

public static class AuthTokenHelper
{
    public const string TestJwtSecret = "this-is-a-super-secret-key-for-testing-only-do-not-use-in-prod";

    public static Task<string> GetTestJwtAsync(string[] permissions = null)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("name", "Test User"),
            new Claim("role", "Admin")
        };

        if (permissions is not null)
        {
            foreach (var perm in permissions)
            {
                claims.Add(new Claim("permissions", perm));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DevJwtBearerFallbackExtensions.DevIssuer,
            audience: DevJwtBearerFallbackExtensions.DevAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds
        );

        return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
    }
}
