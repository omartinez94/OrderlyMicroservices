namespace Identity.API.Extensions;

public static class OpenIddictServerExtensions
{
    /// <summary>
    /// Production-aware OpenIddict registration. Today this method gates
    /// the dev signing/encryption certificates on
    /// <see cref="IHostEnvironment.IsDevelopment"/>; Phase 2 of the
    /// Trust Root Hardening plan layers in PEM/PFX loading for
    /// non-Development environments.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host's IConfiguration.</param>
    /// <param name="environment">The host's IWebHostEnvironment.</param>
    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var jwtSettings = new JwtSettings();
        configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

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

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(jwtSettings.AccessTokenLifetimeMinutes));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(jwtSettings.RefreshTokenLifetimeDays));

                // Gate the dev-only signing/encryption certificates behind
                // IsDevelopment(). In Staging / Production the certs
                // are NOT registered here — Phase 2 introduces the
                // PEM/PFX loader and the OpenIddictCertificateLoadException
                // that fails closed when neither is configured. Until
                // Phase 2 lands, this branch leaves OpenIddict
                // without certs in non-Development environments (the
                // host will fail to issue tokens) — acceptable as an
                // interim state because the bug today is the
                // unconditional dev-cert registration.
                if (environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }

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
