using System.ComponentModel.DataAnnotations;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 4: OpenTelemetry options + the
/// <see cref="CorrelationIdActivityMiddleware"/>-flavoured
/// <see cref="OtelOptions"/> defaults. The actual OTLP export
/// pipeline is integration-tested in Phase 5 (Testcontainers +
/// otel-collector).
/// </summary>
public sealed class OtelOptionsTests
{
    [Fact]
    public void Defaults_DocumentOperationalContract()
    {
        // Lock the documented defaults so a future refactor cannot
        // silently change the OTLP endpoint shape, service name,
        // or version.
        var defaults = new OtelOptions();

        defaults.Enabled.Should().BeTrue();
        defaults.Endpoint.Should().Be("http://localhost:4317");
        defaults.ServiceName.Should().Be("basket.api");
        defaults.ServiceVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void Validation_EmptyEndpoint_Fails()
    {
        // The [Required] data annotation on Endpoint ensures the
        // host refuses to boot with a malformed config. Lock the
        // contract here.
        var opts = new OtelOptions { Endpoint = "" };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OtelOptions.Endpoint)));
    }

    [Fact]
    public void Validation_PopulatedEndpoint_Passes()
    {
        var opts = new OtelOptions { Endpoint = "http://otel-collector:4317" };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
