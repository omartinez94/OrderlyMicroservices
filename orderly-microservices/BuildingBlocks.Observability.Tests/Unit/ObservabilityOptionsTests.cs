using BuildingBlocks.Observability;

namespace BuildingBlocks.Observability.Tests.Unit;

/// <summary>
/// Phase 4: OpenTelemetry options + the
/// <see cref="LoggingBuilderExtensions.AddOrderlyOpenTelemetry"/>-flavoured
/// <see cref="ObservabilityOptions"/> defaults. The actual OTLP export
/// pipeline is integration-tested in <c>OrderlyOpenTelemetryTests</c> via
/// a fake OTLP receiver.
/// </summary>
public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void Defaults_DocumentOperationalContract()
    {
        // Lock the documented defaults so a future refactor cannot
        // silently change the OTLP endpoint shape, service name,
        // version, or logs flag.
        var defaults = new ObservabilityOptions();

        defaults.Enabled.Should().BeTrue();
        defaults.Endpoint.Should().Be("http://localhost:4317");
        defaults.LogsEnabled.Should().BeTrue();
        defaults.ServiceName.Should().BeNull();
        defaults.ServiceVersion.Should().BeNull();
    }

    [Fact]
    public void Validation_EmptyEndpoint_Fails()
    {
        // The [Required] data annotation on Endpoint ensures the
        // host refuses to boot with a malformed config. Lock the
        // contract here.
        var opts = new ObservabilityOptions { Endpoint = "" };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ObservabilityOptions.Endpoint)));
    }

    [Fact]
    public void Validation_PopulatedEndpoint_Passes()
    {
        var opts = new ObservabilityOptions { Endpoint = "http://otel-collector:4317" };
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
