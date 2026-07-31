using System.Security.Cryptography.X509Certificates;

namespace Identity.API.Extensions;

public static class OpenIddictServerExtensions
{
    private const string SigningCertificatePathKey = "OpenIddict:SigningCertificatePath";
    private const string SigningCertificatePasswordKey = "OpenIddict:SigningCertificatePassword";
    private const string EncryptionCertificatePathKey = "OpenIddict:EncryptionCertificatePath";
    private const string EncryptionCertificatePasswordKey = "OpenIddict:EncryptionCertificatePassword";

    /// <summary>
    /// Production-aware OpenIddict registration. Dev (Development
    /// environment) uses the auto-generated certs from
    /// <c>AddDevelopmentSigningCertificate</c>; non-Development loads
    /// PEM/PFX files referenced by <c>OpenIddict:SigningCertificatePath</c>
    /// and the encryption equivalent. Missing files fail-closed via
    /// <see cref="OpenIddictCertificateLoadException"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host's IConfiguration.</param>
    /// <param name="environment">The host's IWebHostEnvironment.</param>
    /// <exception cref="OpenIddictCertificateLoadException">
    /// Thrown when a non-Development environment references a
    /// signing/encryption certificate path that is missing or
    /// unreadable. Fail-closed: the host refuses to start rather than
    /// booting with an unsigned OpenIddict server.
    /// </exception>
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

                if (environment.IsDevelopment())
                {
                    // Dev certs are ephemeral and per-host. /root/.aspnet/https is
                    // mounted writable in docker-compose.override.yml so they
                    // survive container restarts (Phase 2 deliverable).
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // Production/Staging: load PEM/PFX from config-referenced
                    // paths. Fail closed on missing or unreadable files.
                    RegisterProductionCertificate(options, configuration, SigningCertificatePathKey, SigningCertificatePasswordKey, isEncryption: false);
                    RegisterProductionCertificate(options, configuration, EncryptionCertificatePathKey, EncryptionCertificatePasswordKey, isEncryption: true);
                }

                // UseAspNetCore wires Kestrel/HTTPS handling. Per Phase 2 we
                // drop DisableTransportSecurityRequirement() so the
                // authorization_code and refresh_token grant types cannot
                // travel over plain HTTP outside Development. In Development
                // the Kestrel certificate (ASPNETCORE_Kestrel__Certificates__Default__Path)
                // makes HTTPS available so the token endpoint still works.
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    private static void RegisterProductionCertificate(
        OpenIddictServerBuilder options,
        IConfiguration configuration,
        string pathKey,
        string passwordKey,
        bool isEncryption)
    {
        var certLabel = isEncryption ? "encryption" : "signing";

        var path = configuration[pathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new OpenIddictCertificateLoadException(
                $"{pathKey} is required in non-Development environments. " +
                $"Set it to an absolute path to a PEM or PKCS#12 (PFX) file containing the OpenIddict {certLabel} certificate. " +
                $"Example: \"OpenIddict:{char.ToUpper(certLabel[0])}{certLabel[1..]}CertificatePath\": \"/etc/openiddict/{certLabel}.pfx\".");
        }

        if (!File.Exists(path))
        {
            throw new OpenIddictCertificateLoadException(
                $"OpenIddict {certLabel} certificate file not found at '{path}'. " +
                $"Verify {pathKey} points to an existing PEM or PFX file readable by the host process.");
        }

        var password = configuration[passwordKey];

        try
        {
            // OpenIddict 7.5's Stream-based loader only accepts PFX/PKCS#12.
            // For PEM we load via X509Certificate2.CreateFromPemFile and pass
            // the cert through the X509Certificate2-based overload.
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".pem" or ".crt" or ".cer" or ".key")
            {
                var cert = X509Certificate2.CreateFromPemFile(path, ReadKeyFileOrNull(path));
                if (isEncryption)
                    options.AddEncryptionCertificate(cert);
                else
                    options.AddSigningCertificate(cert);
            }
            else
            {
                // PKCS#12 / PFX path
                using var stream = File.OpenRead(path);
                if (isEncryption)
                    options.AddEncryptionCertificate(stream, password);
                else
                    options.AddSigningCertificate(stream, password);
            }
        }
        catch (OpenIddictCertificateLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OpenIddictCertificateLoadException(
                $"Failed to register OpenIddict {certLabel} certificate from '{path}'. " +
                $"Ensure the file is a valid PEM or PKCS#12 (PFX) and the password in {passwordKey} matches. Inner: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// For a PEM cert path like <c>cert.pem</c>, looks for a sibling
    /// <c>cert.key</c> (the conventional private-key companion file)
    /// and returns its path. Returns <c>null</c> when no companion
    /// exists — the PEM file may embed the key, in which case
    /// <c>X509Certificate2.CreateFromPemFile</c> ignores the key path.
    /// </summary>
    private static string? ReadKeyFileOrNull(string certPath)
    {
        var dir = Path.GetDirectoryName(certPath);
        var name = Path.GetFileNameWithoutExtension(certPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
            return null;

        foreach (var keyExt in new[] { ".key", "-key.pem", ".key.pem" })
        {
            var keyPath = Path.Combine(dir, name + keyExt);
            if (File.Exists(keyPath))
                return keyPath;
        }
        return null;
    }
}
