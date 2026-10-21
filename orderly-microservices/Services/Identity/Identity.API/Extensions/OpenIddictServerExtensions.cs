using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

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

        JwtSettings? jwtSettings = new JwtSettings();
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
                    .SetTokenEndpointUris("/connect/token", "/v1/auth/refresh", "/api/auth/refresh");

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.OfflineAccess);

                options.AllowPasswordFlow();

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                options.AllowRefreshTokenFlow();
                options.UseReferenceRefreshTokens();
                options.DisableAccessTokenEncryption();

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(jwtSettings.AccessTokenLifetimeMinutes));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(jwtSettings.RefreshTokenLifetimeDays));

                options.AddEventHandler<OpenIddictServerEvents.ExtractTokenRequestContext>(eventBuilder =>
                {
                    eventBuilder.UseInlineHandler(context =>
                    {
                        HttpRequest? request = context.Transaction.GetHttpRequest();
                        if (request is not null && context.Request is not null)
                        {
                            if (string.IsNullOrEmpty(context.Request.GrantType))
                            {
                                context.Request.GrantType = OpenIddictConstants.GrantTypes.RefreshToken;
                            }

                            if (string.IsNullOrEmpty(context.Request.RefreshToken) &&
                                request.Cookies.TryGetValue("refresh_token", out string? refreshToken) &&
                                !string.IsNullOrEmpty(refreshToken))
                            {
                                context.Request.RefreshToken = refreshToken;
                            }
                        }

                        return default;
                    });
                });

                options.AddEventHandler<OpenIddictServerEvents.ApplyTokenResponseContext>(eventBuilder =>
                {
                    eventBuilder.UseInlineHandler(context =>
                    {
                        HttpRequest? request = context.Transaction.GetHttpRequest();
                        HttpResponse? response = request?.HttpContext.Response;

                        if (response is null)
                        {
                            return default;
                        }

                        if (!string.IsNullOrEmpty(context.Response.Error))
                        {
                            if (request?.Cookies.ContainsKey("refresh_token") == true)
                            {
                                response.Cookies.Delete("refresh_token", new CookieOptions
                                {
                                    HttpOnly = true,
                                    Secure = request.IsHttps,
                                    SameSite = SameSiteMode.Lax,
                                    Path = "/"
                                });
                            }
                            return default;
                        }

                        string? refreshToken = (string?)context.Response[OpenIddictConstants.Parameters.RefreshToken];
                        if (!string.IsNullOrEmpty(refreshToken))
                        {
                            response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = request!.IsHttps,
                                SameSite = SameSiteMode.Lax,
                                Path = "/",
                                MaxAge = TimeSpan.FromDays(jwtSettings.RefreshTokenLifetimeDays),
                                Expires = DateTimeOffset.UtcNow.AddDays(jwtSettings.RefreshTokenLifetimeDays)
                            });
                        }

                        string? accessToken = (string?)context.Response[OpenIddictConstants.Parameters.AccessToken];
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            context.Response.SetParameter("accessToken", accessToken);

                            long? expiresIn = (long?)context.Response[OpenIddictConstants.Parameters.ExpiresIn];
                            long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (expiresIn ?? 900) * 1000;
                            context.Response.SetParameter("expiresAt", expiresAt);

                            if (request?.HttpContext.Items.TryGetValue("AuthResponse_User", out object? userObj) == true && userObj is JsonElement userJson)
                            {
                                context.Response.SetParameter("user", userJson);
                            }

                            if (request?.HttpContext.Items.TryGetValue("AuthResponse_Roles", out object? rolesObj) == true && rolesObj is JsonElement rolesJson)
                            {
                                context.Response.SetParameter("roles", rolesJson);
                            }

                            if (request?.HttpContext.Items.TryGetValue("AuthResponse_Permissions", out object? permsObj) == true && permsObj is JsonElement permsJson)
                            {
                                context.Response.SetParameter("permissions", permsJson);
                            }
                        }

                        return default;
                    });
                });

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

                // UseAspNetCore wires Kestrel/HTTPS handling.
                //
                // Non-Development (Production/Staging): HTTPS is enforced
                // by OpenIddict's transport security requirement, so the
                // authorization_code + refresh_token grant types cannot
                // travel over plain HTTP. In Development: the docker
                // gateway forwards /identity-api → http://identity.api:8080
                // (plain HTTP — see yarpapigateway's `identity-cluster`
                // in appsettings.json), and the internal /v1/auth/login →
                // /connect/token forward (LoginModule.cs) is also HTTP.
                // Requiring HTTPS end-to-end inside the dev compose would
                // force every developer to either trust the dev cert or
                // stand up TLS termination in the gateway. Production
                // posture below stays locked — see the non-Development
                // branch.
                if (environment.IsDevelopment())
                {
                    // /v1/auth/login + /connect/token's password grant are
                    // designed for end-user login (the user IS the
                    // authenticator, not a client). Neither seeded client
                    // (orderly-spa — public + PKCE; orderly-service-bus —
                    // M2M + confidential) has the GrantTypes.Password
                    // permission, and shipping client credentials through
                    // a public login endpoint would be a regression.
                    // AcceptAnonymousClients lets the password grant
                    // proceed without a client_id. Production posture
                    // below stays strict — clients must authenticate.
                    options.AcceptAnonymousClients();

                    options.UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableTokenEndpointPassthrough()
                        .DisableTransportSecurityRequirement();
                }
                else
                {
                    options.UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableTokenEndpointPassthrough();
                }
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
        string? certLabel = isEncryption ? "encryption" : "signing";

        string? path = configuration[pathKey];
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

        string? password = configuration[passwordKey];

        try
        {
            // The encryption vs signing choice is a single branch — both
            // PEM (X509Certificate2-based) and PKCS#12 (Stream-based)
            // overloads come in matching pairs, so we factor the choice
            // into two Action delegates and let the cert-format gate
            // pick the right one.
            Action<X509Certificate2> addCert = isEncryption
                ? cert => options.AddEncryptionCertificate(cert)
                : cert => options.AddSigningCertificate(cert);
            Action<Stream, string?> addCertFromStream = isEncryption
                ? (stream, pwd) => options.AddEncryptionCertificate(stream, pwd)
                : (stream, pwd) => options.AddSigningCertificate(stream, pwd);

            // OpenIddict 7.5's Stream-based loader only accepts PFX/PKCS#12.
            // For PEM we load via X509Certificate2.CreateFromPemFile and pass
            // the cert through the X509Certificate2-based overload.
            string? ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".pem" or ".crt" or ".cer" or ".key")
            {
                X509Certificate2 cert = X509Certificate2.CreateFromPemFile(path, ReadKeyFileOrNull(path));
                addCert(cert);
            }
            else
            {
                // PKCS#12 / PFX path
                using var stream = File.OpenRead(path);
                addCertFromStream(stream, password);
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
        string? dir = Path.GetDirectoryName(certPath);
        string? name = Path.GetFileNameWithoutExtension(certPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (string? keyExt in new[] { ".key", "-key.pem", ".key.pem" })
        {
            string? keyPath = Path.Combine(dir, name + keyExt);
            if (File.Exists(keyPath))
            {
                return keyPath;
            }
        }
        return null;
    }
}
