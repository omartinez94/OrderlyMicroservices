using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildingBlocks.Dev.Tests;

/// <summary>
/// Verifies the <see cref="DevTriggerEndpointExtensions"/> gate:
/// secret presence on the host, header presence on the request, and
/// constant-time equality between the two. The full integration
/// story (MapPost + middleware) is covered by the service-level
/// tests; these tests pin the comparison primitive.
/// </summary>
public class DevTriggerEndpointTests
{
    private const string Secret = "dev-trigger-secret-at-least-16-chars";

    [Fact]
    public async Task ValidateSecret_NoEnvVar_Returns503()
    {
        var prev = Environment.GetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar);
        Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, null);

        try
        {
            var ctx = BuildContext();
            var ok = await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, CancellationToken.None);

            ok.Should().BeFalse();
            ctx.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, prev);
        }
    }

    [Fact]
    public async Task ValidateSecret_HeaderMissing_Returns401()
    {
        Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, Secret);
        var ctx = BuildContext();

        var ok = await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, CancellationToken.None);

        ok.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task ValidateSecret_HeaderMatches_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, Secret);
        var ctx = BuildContext(secretValue: Secret);

        var ok = await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, CancellationToken.None);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSecret_HeaderMismatches_Returns401()
    {
        Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, Secret);
        var ctx = BuildContext(secretValue: "wrong-secret-also-long-enough");

        var ok = await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, CancellationToken.None);

        ok.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task ValidateSecret_ShortMismatchedPrefix_StillReturns401()
    {
        // Pin the constant-time equality: a partial prefix match
        // must not produce a short-circuit success. CryptographicOperations.FixedTimeEquals
        // returns false on length mismatch, but verify behaviorally.
        Environment.SetEnvironmentVariable(DevTriggerEndpointExtensions.SecretEnvVar, Secret);
        var ctx = BuildContext(secretValue: Secret.Substring(0, 5));

        var ok = await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, CancellationToken.None);

        ok.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private static HttpContext BuildContext(string? secretValue = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));

        var ctx = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };

        ctx.Response.Body = new MemoryStream();

        if (secretValue is not null)
        {
            ctx.Request.Headers[DevTriggerEndpointExtensions.SecretHeader] = secretValue;
        }

        return ctx;
    }
}