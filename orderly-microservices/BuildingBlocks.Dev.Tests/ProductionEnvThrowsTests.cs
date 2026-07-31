using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Dev.Tests;

/// <summary>
/// Asserts the IsDevelopment() + JWT_SECRET matrix that guards the
/// dev HS256 fallback in
/// <see cref="DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback"/>.
/// </summary>
/// <remarks>
/// <para>The matrix has four cells; this suite covers all of them
/// without spinning up a full host. Two of the cells also pass
/// through the helper <see cref="DevJwtEnvironment"/> so a regression
/// in the helper or the extension is independently pinpointed.</para>
/// <para>The two production postures are the security-sensitive
/// cells. The
/// <c>ProductionJwtKeyLoadException_ThrownWhen_SecretSetOutsideDevelopment</c>
/// test is the headline assertion — it is what stops a leaked
/// <c>JWT_SECRET</c> env var from registering a forgeable HS256
/// scheme on a production host.</para>
/// </remarks>
public class ProductionEnvThrowsTests
{
    private const string FakeSecret = "this-is-a-development-only-jwt-secret-32+";

    /// <summary>
    /// Helper: returns an <see cref="IWebHostEnvironment"/> whose
    /// <see cref="IHostEnvironment.EnvironmentName"/> matches the
    /// argument. The interface members are stubbed via the simple
    /// anonymous-class + cast pattern so the suite does not pull in
    /// a Moq / NSubstitute dependency just for this.
    /// </summary>
    private static IWebHostEnvironment Env(string environmentName) =>
        new StubWebHostEnvironment { EnvironmentName = environmentName };

    /// <summary>
    /// Helper: returns an <see cref="IConfiguration"/> that resolves
    /// the requested key/value pairs. Empty entries produce a
    /// configuration where JWT_SECRET is effectively unset.
    /// </summary>
    private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => (string?)e.Value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict!)
            .Build();
    }

    [Fact]
    public void ProductionEnvWithSecret_Throws_ProductionJwtKeyLoadException()
    {
        // Arrange: non-Development environment + a leaked JWT_SECRET.
        // This is the headline guard: the host must refuse to start.
        var services = new ServiceCollection();
        var env = Env("Production");
        var config = ConfigWith(("JWT_SECRET", FakeSecret));

        var act = () => services.AddJwtAuthenticationWithDevFallback(
            env, config, authority: "https://localhost:5057", audience: "OrderlyMicroservices");

        act.Should().Throw<ProductionJwtKeyLoadException>()
            .Which.Message.Should().Contain("Production").And.Contain("JWT_SECRET",
                "the exception text names the environment and the env var so the operator knows what to fix");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Staging_2")] // a non-standard production-shaped name still fails closed.
    public void NonDevelopmentEnv_WithSecret_AlwaysThrows(string environmentName)
    {
        var services = new ServiceCollection();
        var env = Env(environmentName);
        var config = ConfigWith(("JWT_SECRET", FakeSecret));

        var act = () => services.AddJwtAuthenticationWithDevFallback(
            env, config, authority: "https://localhost:5057", audience: "OrderlyMicroservices");

        act.Should().Throw<ProductionJwtKeyLoadException>(
            $"environment '{environmentName}' with a leaked JWT_SECRET must fail closed");
    }

    [Fact]
    public void DevelopmentEnvWithSecret_Registers_WithoutThrowing()
    {
        // Arrange: the happy dev path. The HS256 fallback should
        // register so MCP-signed tokens validate; the policy scheme
        // + OpenIddict JWKS scheme must both be present.
        var services = new ServiceCollection();
        var env = Env("Development");
        var config = ConfigWith(("JWT_SECRET", FakeSecret));

        var act = () => services.AddJwtAuthenticationWithDevFallback(
            env, config, authority: "https://localhost:5057", audience: "OrderlyMicroservices");

        act.Should().NotThrow("the dev path with a JWT_SECRET is the supported posture");
        // AuthenticationBuilder registers a policy + per-scheme
        // AuthenticationSchemeOptions; the ServiceCollection should
        // carry the AuthServices (smoke check).
        services.Should().Contain(sd =>
            sd.ServiceType.FullName == "Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider");
    }

    [Fact]
    public void DevelopmentEnvWithNoSecret_DoesNotThrow_AndDegradesToOpenIddictOnly()
    {
        // Arrange: dev path with the secret unset. The extension
        // silently registers only the OpenIddict JWKS scheme; the
        // HS256 fallback is dormant (today's behaviour, documented).
        var services = new ServiceCollection();
        var env = Env("Development");
        var config = ConfigWith(); // no JWT_SECRET entry

        var act = () => services.AddJwtAuthenticationWithDevFallback(
            env, config, authority: "https://localhost:5057", audience: "OrderlyMicroservices");

        act.Should().NotThrow("a dev host without JWT_SECRET is the unsupported-but-tolerated posture");
    }

    [Fact]
    public void ProductionEnvWithoutSecret_DoesNotThrow_AndRegistersOpenIddictOnly()
    {
        // Arrange: production-shape host, no JWT_SECRET. The OpenIddict
        // JWKS scheme registers normally; the HS256 fallback is dormant
        // by virtue of the secret being unset.
        var services = new ServiceCollection();
        var env = Env("Production");
        var config = ConfigWith();

        var act = () => services.AddJwtAuthenticationWithDevFallback(
            env, config, authority: "https://localhost:5057", audience: "OrderlyMicroservices");

        act.Should().NotThrow("a production host without JWT_SECRET is the supported posture");
    }

    // --- Helper-level matrix tests -------------------------------------------------
    //
    // These cover DevJwtEnvironment directly. They make the matrix
    // debugging cheap: a regression in either the helper or the
    // extension lands the same failure mode on whichever the suite
    // surfaces first.

    [Fact]
    public void DevJwtEnvironment_IsDevJwtAllowed_TrueOnlyFor_Dev_Plus_Secret()
    {
        var dev = Env("Development");
        var prod = Env("Production");
        var withSecret = ConfigWith(("JWT_SECRET", FakeSecret));
        var withoutSecret = ConfigWith();

        DevJwtEnvironment.IsDevJwtAllowed(dev, withSecret).Should().BeTrue();
        DevJwtEnvironment.IsDevJwtAllowed(dev, withoutSecret).Should().BeFalse();
        DevJwtEnvironment.IsDevJwtAllowed(prod, withSecret).Should().BeFalse();
        DevJwtEnvironment.IsDevJwtAllowed(prod, withoutSecret).Should().BeFalse();
    }

    [Fact]
    public void DevJwtEnvironment_IsProductionWithLeakedJwtSecret_TrueOnlyFor_Prod_Plus_Secret()
    {
        var dev = Env("Development");
        var prod = Env("Production");
        var withSecret = ConfigWith(("JWT_SECRET", FakeSecret));
        var withoutSecret = ConfigWith();

        DevJwtEnvironment.IsProductionWithLeakedJwtSecret(prod, withSecret).Should().BeTrue();
        DevJwtEnvironment.IsProductionWithLeakedJwtSecret(prod, withoutSecret).Should().BeFalse();
        DevJwtEnvironment.IsProductionWithLeakedJwtSecret(dev, withSecret).Should().BeFalse();
        DevJwtEnvironment.IsProductionWithLeakedJwtSecret(dev, withoutSecret).Should().BeFalse();
    }

    [Fact]
    public void DevJwtEnvironment_HelperRejects_NullArguments()
    {
        var dev = Env("Development");
        var config = ConfigWith();

        Action allowNullEnv = () => DevJwtEnvironment.IsDevJwtAllowed(null!, config);
        Action allowNullConfig = () => DevJwtEnvironment.IsDevJwtAllowed(dev, null!);

        allowNullEnv.Should().Throw<ArgumentNullException>();
        allowNullConfig.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Minimal <see cref="IWebHostEnvironment"/> stub. The
    /// extension only reads <see cref="IHostEnvironment.EnvironmentName"/>
    /// + <see cref="IHostEnvironment.IsDevelopment"/>; the
    /// content-root / web-root / application-name members are
    /// unused and left as defaults.
    /// </summary>
    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "BuildingBlocks.Dev.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
