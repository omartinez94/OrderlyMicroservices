namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies the /live + /ready health-check split. /live must always
/// report 200 (process-up only; no checks attached). /ready must report
/// 200 when the readiness probes are healthy and 503 once any probe
/// under the "ready" tag trips. Plan §7 Phase 1 + §0.4.5 health split.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class HealthCheckSplitTests(DiscountWebApplicationFactory factory)
{
    [Fact]
    public async Task Live_AlwaysReturns_OK()
    {
        var http = factory.CreateClient();

        var response = await http.GetAsync("/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "/live has no checks attached (Predicate = _ => false), so the liveness probe is always green");
    }

    [Fact]
    public async Task Ready_HealthyState_Returns_OK()
    {
        // Ensure no transacted data is shaping the dead-letter probe.
        await factory.CleanAllAsync();
        var http = factory.CreateClient();

        var response = await http.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "/ready reports 200 when no readiness probe has tripped");
    }

    [Fact]
    public async Task Ready_DeadLetterThresholdExceeded_Returns_503()
    {
        await factory.CleanAllAsync();

        var threshold = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscountOptions>>()
            .Value.OutboxDeadLetterThreshold;

        // Insert threshold + 1 dead rows so the probe count exceeds
        // threshold. The OutboxDeadMessage row payload isn't constrained
        // by the live code path; the probe just counts.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            for (var i = 0; i < threshold + 1; i++)
            {
                db.OutboxDeadMessages.Add(new OutboxDeadMessage
                {
                    Id = Guid.NewGuid(),
                    OccurredOn = NodaTime.SystemClock.Instance.GetCurrentInstant(),
                    Type = "TestEvent",
                    Payload = "{}",
                    SchemaVersion = 1,
                    Reason = "test-seed",
                    RejectedAt = NodaTime.SystemClock.Instance.GetCurrentInstant(),
                });
            }
            await db.SaveChangesAsync();
        }

        var http = factory.CreateClient();
        var response = await http.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            $"/ready reports Unhealthy when OutboxDeadMessages rows exceed {threshold}");
    }
}
