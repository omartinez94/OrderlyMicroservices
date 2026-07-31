using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Identity.API.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Identity.API.Tests.Extensions;

/// <summary>
/// Asserts the production-cert posture of
/// <see cref="OpenIddictServerExtensions.AddOpenIddictServer"/>:
/// non-Development environments must reference a configured
/// signing/encryption certificate, and the file must be readable.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>BuildingBlocks.Dev.Tests/ProductionEnvThrowsTests</c>:
/// the suite calls the extension directly with a fake
/// <see cref="IWebHostEnvironment"/> and an in-memory
/// <see cref="IConfiguration"/>, no full host, no Postgres
/// Testcontainers.</para>
/// <para>The headline assertions are the "missing cert path" and
/// "missing cert file" cells — these are the production posture
/// regressions that the <see cref="OpenIddictCertificateLoadException"/>
/// exists to catch.</para>
/// </remarks>
public class OpenIddictServerEnvGateTests : IDisposable
{
    private readonly string _tempDir;

    public OpenIddictServerEnvGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "oidc-cert-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup; not a test concern
        }
    }

    private static IWebHostEnvironment Env(string environmentName) =>
        new StubWebHostEnvironment { EnvironmentName = environmentName };

    private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => (string?)e.Value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict!)
            .Build();
    }

    [Fact]
    public void NonDevelopment_MissingSigningCertPath_Throws()
    {
        // Arrange: production-shaped env, no OpenIddict:SigningCertificatePath set.
        // The extension must fail closed at registration time.
        var services = new ServiceCollection();
        var env = Env("Production");
        var config = ConfigWith(); // empty

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().Throw<OpenIddictCertificateLoadException>()
            .Which.Message.Should()
            .Contain("OpenIddict:SigningCertificatePath",
                "the exception text must name the missing config key so the operator can fix it");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopment_AnyEnvName_MissingCert_AlwaysThrows(string environmentName)
    {
        var services = new ServiceCollection();
        var env = Env(environmentName);
        var config = ConfigWith();

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().Throw<OpenIddictCertificateLoadException>(
            $"environment '{environmentName}' without a configured cert must fail closed");
    }

    [Fact]
    public void NonDevelopment_CertPathSetButFileMissing_Throws()
    {
        // Arrange: path is set, but the file does not exist on disk.
        var services = new ServiceCollection();
        var env = Env("Production");
        var missingPath = Path.Combine(_tempDir, "does-not-exist.pfx");
        var config = ConfigWith(
            ("OpenIddict:SigningCertificatePath", missingPath),
            ("OpenIddict:EncryptionCertificatePath", missingPath));

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().Throw<OpenIddictCertificateLoadException>()
            .Which.Message.Should()
            .Contain(missingPath, "the exception must echo the path so the operator can verify it");
    }

    [Fact]
    public void Development_MissingCertPath_DoesNotThrow_UsesDevCerts()
    {
        // Arrange: the dev happy path. No cert paths needed because
        // AddDevelopmentSigningCertificate is used.
        var services = new ServiceCollection();
        var env = Env("Development");
        var config = ConfigWith();

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().NotThrow(
            "the dev path registers dev certs and does not require OpenIddict:* config");
    }

    [Fact]
    public void NonDevelopment_ValidPfxCert_DoesNotThrow_RegistersCert()
    {
        // Arrange: produce a self-signed cert at a temp path.
        // Use it for both signing and encryption so the test stays minimal.
        var pfxPath = Path.Combine(_tempDir, "test.pfx");
        const string password = "test-pwd-12345";
        WriteSelfSignedPfx(pfxPath, password);

        var services = new ServiceCollection();
        var env = Env("Production");
        var config = ConfigWith(
            ("OpenIddict:SigningCertificatePath", pfxPath),
            ("OpenIddict:SigningCertificatePassword", password),
            ("OpenIddict:EncryptionCertificatePath", pfxPath),
            ("OpenIddict:EncryptionCertificatePassword", password));

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().NotThrow("a valid PFX cert referenced from config should register cleanly");
    }

    [Fact]
    public void NonDevelopment_PemCert_WithoutPassword_Registers()
    {
        // Arrange: PEM is supported by OpenIddict when the password is null/empty.
        var pemPath = Path.Combine(_tempDir, "test.pem");
        WriteSelfSignedPem(pemPath);

        var services = new ServiceCollection();
        var env = Env("Production");
        var config = ConfigWith(
            ("OpenIddict:SigningCertificatePath", pemPath),
            // password intentionally unset — PEMs typically don't have one
            ("OpenIddict:EncryptionCertificatePath", pemPath));

        var act = () => services.AddOpenIddictServer(config, env);

        act.Should().NotThrow("a PEM cert without a password should register cleanly");
    }

    [Fact]
    public void AddOpenIddictServer_Rejects_NullArguments()
    {
        var services = new ServiceCollection();
        var config = ConfigWith();
        var env = Env("Development");

        Action nullServices = () => Identity.API.Extensions.OpenIddictServerExtensions.AddOpenIddictServer(null!, config, env);
        Action nullConfig = () => services.AddOpenIddictServer(null!, env);
        Action nullEnv = () => services.AddOpenIddictServer(config, null!);

        nullServices.Should().Throw<ArgumentNullException>();
        nullConfig.Should().Throw<ArgumentNullException>();
        nullEnv.Should().Throw<ArgumentNullException>();
    }

    private static void WriteSelfSignedPfx(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=orderly-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(path, pfxBytes);
    }

    private static void WriteSelfSignedPem(string path)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=orderly-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        // Export to PEM (returns both CERT and KEY sections concatenated).
        var pem = "-----BEGIN CERTIFICATE-----\n" +
                  Convert.ToBase64String(cert.Export(X509ContentType.Cert), Base64FormattingOptions.InsertLineBreaks) +
                  "\n-----END CERTIFICATE-----\n" +
                  "-----BEGIN PRIVATE KEY-----\n" +
                  Convert.ToBase64String(cert.GetRSAPrivateKey()!.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) +
                  "\n-----END PRIVATE KEY-----\n";
        File.WriteAllText(path, pem);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Identity.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
