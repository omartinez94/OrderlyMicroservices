namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies <see cref="BrokerHealthState"/> drives the <c>/ready</c>
/// health endpoint. Discount defines the convention; Catalog follows.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class DiscountOutboxCircuitBreakerTests(DiscountWebApplicationFactory factory)
{
    [Fact]
    public async Task ThreeConsecutiveFailures_TripsReady_To503()
    {
        var threshold = ReadBrokerThreshold();
        var http = factory.CreateClient();

        // Healthy start: /ready returns 200.
        var healthy = await http.GetAsync("/ready");
        healthy.StatusCode.Should().Be(HttpStatusCode.OK);

        // Trip the circuit past threshold.
        using (factory.TripBrokerCircuit(threshold))
        {
            var tripped = await http.GetAsync("/ready");
            tripped.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                $"/ready reports Unhealthy once BrokerHealthState.ConsecutiveBrokerFailures >= {threshold}");
        }

        // After TripBrokerCircuit disposes, the state is Reset → 200.
        var recovered = await http.GetAsync("/ready");
        recovered.StatusCode.Should().Be(HttpStatusCode.OK,
            "TripBrokerCircuit's IDisposable restores the prior healthy state");
    }

    [Fact]
    public async Task BelowThreshold_ReadyStaysHealthy()
    {
        var threshold = ReadBrokerThreshold();
        var http = factory.CreateClient();

        // One failure below threshold: state is broker-unhealthy but
        // /ready doesn't trip (because IsHealthy(threshold) is true).
        var state = factory.Services.GetRequiredService<BrokerHealthState>();
        var before = state.ConsecutiveBrokerFailures;
        state.RecordFailure();
        var after = state.ConsecutiveBrokerFailures;
        after.Should().Be(before + 1);

        var response = await http.GetAsync("/ready");
        // One failure is below threshold (3) so the broker-circuit probe
        // reports Healthy; the dead-letter probe never tripped, so
        // /ready stays 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "one failure below threshold doesn't flip /ready");

        state.Reset();
    }

    [Fact]
    public async Task RecordFailure_AndReset_RoundTrip()
    {
        // Pure unit-style probe — drives the singleton directly to
        // verify the counter math + reset semantics. Cheap, no HTTP,
        // no DB.
        await using var scope = factory.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<BrokerHealthState>();

        state.Reset();
        state.ConsecutiveBrokerFailures.Should().Be(0);

        var one = state.RecordFailure();
        var two = state.RecordFailure();
        var three = state.RecordFailure();

        one.Should().Be(1);
        two.Should().Be(2);
        three.Should().Be(3);

        state.Reset();
        state.ConsecutiveBrokerFailures.Should().Be(0);
    }

    /// <summary>Reads the broker-circuit threshold the dispatcher uses.
    /// Lives on <see cref="DiscountOptions"/> by convention
    /// (broker circuit + dead letter share the threshold knob).</summary>
    private int ReadBrokerThreshold()
    {
        var options = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscountOptions>>()
            .Value;
        // The dead-letter threshold doubles as the broker-circuit
        // threshold (the spec puts both at 3 default; relaxing this
        // would require splitting the knobs).
        var threshold = options.OutboxDeadLetterThreshold > 0
            ? options.OutboxDeadLetterThreshold
            : 3;
        return threshold;
    }
}
