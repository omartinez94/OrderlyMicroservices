using OpenIddict.Abstractions;

namespace Identity.API.Extensions;

public static class OpenIddictServerExtensions
{
    public static IServiceCollection AddOpenIddictServer(this IServiceCollection services, IConfiguration configuration)
    {
        var accessTokenLifetime = int.Parse(configuration["Jwt:AccessTokenLifetimeMinutes"] ?? "15");
        var refreshTokenLifetime = int.Parse(configuration["Jwt:RefreshTokenLifetimeDays"] ?? "7");

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<Data.IdentityDbContext>();
            })
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token");

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.OfflineAccess);

                options.AllowPasswordFlow();

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                options.AllowRefreshTokenFlow();

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(accessTokenLifetime));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(refreshTokenLifetime));

                options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }
}
