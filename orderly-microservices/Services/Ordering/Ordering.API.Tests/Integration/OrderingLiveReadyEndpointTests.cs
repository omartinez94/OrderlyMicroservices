using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Phase 5 of PERSISTENCE_AND_RELIABILITY_PLAN.md (plan §6.5).
///
/// Replaces the pre-Phase-5 single <c>/health</c> endpoint with the
/// standard Kubernetes split:
/// <list type="bullet">
/// <item><c>/live</c> is liveness-only — always 200 (process up). Kubernetes
/// uses this to decide whether to <em>restart</em> the pod.</item>
/// <item><c>/ready</c> is readiness-only — aggregates every health check
/// tagged <c>"ready"</c>. Kubernetes uses this to decide whether to
/// <em>remove the pod from load-balancer rotation</em>.</item>
/// </list>
///
/// The pre-Phase-5 <c>/health</c> endpoint conflated the two semantics; a
/// transient MSSQL blip would 503 the liveness probe and Kubernetes would
/// restart the pod — a needless recovery cycle. The split is the standard
/// pattern. The MSSQL check in
/// <c>Ordering.API/DependencyInjection.cs</c> is tagged <c>"ready"</c>; the
/// broker + outbox DLQ checks (added in subsequent plans) will inherit the
/// same tag.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingLiveReadyEndpointTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingLiveReadyEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// <c>/live</c> always returns 200 — the predicate
    /// <c>_ =&gt; false</c> skips every check, so the endpoint
    /// returns Healthy regardless of backing-store state. This is the
    /// contract: liveness = process alive, nothing else.
    /// </summary>
    [Fact]
    public async Task Live_Always_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/live");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        // The body is the standard HealthCheckUIResponse JSON shape.
        // `status` must be Healthy, `entries` empty (no checks ran).
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status")
            .GetString()
            .Should().Be("Healthy");
        doc.RootElement.GetProperty("entries")
            .EnumerateObject()
            .Should()
            .BeEmpty("liveness must not consult any backing store");
    }

    /// <summary>
    /// <c>/ready</c> returns 200 when every <c>"ready"</c>-tagged check
    /// is Healthy. In the test fixture the MSSQL Testcontainer is up
    /// and the broker Testcontainer is up, so the only
    /// <c>"ready"</c>-tagged check (the MSSQL connection) reports
    /// Healthy.
    /// </summary>
    [Fact]
    public async Task Ready_BackingStoresUp_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status")
            .GetString()
            .Should().Be("Healthy");

        // The MSSQL check registered in DependencyInjection.cs is
        // named "sqlserver" by the package default. Phase 5 tagged it
        // with "ready" so it appears in the readiness probe output.
        var entries = doc.RootElement.GetProperty("entries");
        var sqlServerEntry = entries.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals("sqlserver", StringComparison.OrdinalIgnoreCase));
        sqlServerEntry.Value.GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");
    }

    /// <summary>
    /// <c>/ready</c> returns 503 when a <c>"ready"</c>-tagged check is
    /// Unhealthy. We exercise this by reporting a custom Unhealthy
    /// status via the in-memory <see cref="HealthCheckService"/> after
    /// stopping the broker — the broker RabbitMQ container is brought
    /// down out-of-band (or, more portably, by removing its connection
    /// string from config and asking the test WAF to rebuild the host).
    /// </summary>
    /// <remarks>
    /// The fixture does not tear down the RabbitMQ container mid-test
    /// (the collection fixture shares it across tests). Instead this
    /// test registers an additional Unhealthy check on the test host
    /// and verifies that <c>/ready</c> returns 503 while <c>/live</c>
    /// keeps returning 200. This is the load-bearing distinction: a
    /// blip in a backing store must NOT trip the liveness probe.
    /// </remarks>
    [Fact]
    public async Task Ready_BackingStoreDown_Returns503_AndLiveStaysGreen()
    {
        // Build a one-off test host that registers an extra
        // "simulated-broker-down" check tagged "ready" with a known
        // Unhealthy status. We don't reuse the shared collection
        // fixture for this test because the shared fixture's WAF
        // doesn't expose a hook to add ad-hoc checks.
        using var localFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHealthChecks()
                    .AddCheck(
                        name: "simulated-broker-down",
                        instance: new AlwaysUnhealthyCheck(
                            "Simulated RabbitMQ outage for /ready endpoint test"),
                        failureStatus: HealthStatus.Unhealthy,
                        tags: new[] { "ready" });
            });
        });

        var readyClient = localFactory.CreateClient();
        var liveClient = localFactory.CreateClient();

        var readyResponse = await readyClient.GetAsync("/ready");
        readyResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // /live MUST stay 200 — the predicate `_ => false` skips every
        // check, including the simulated-broker-down one.
        var liveResponse = await liveClient.GetAsync("/live");
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "liveness must not consult any backing store, even an unhealthy one");
    }

    /// <summary>
    /// Always-unhealthy probe. Used by
    /// <see cref="Ready_BackingStoreDown_Returns503_AndLiveStaysGreen"/>
    /// to simulate a backing-store outage without tearing down a
    /// shared Testcontainer.
    /// </summary>
    private sealed class AlwaysUnhealthyCheck : IHealthCheck
    {
        private readonly string _description;

        public AlwaysUnhealthyCheck(string description)
        {
            _description = description;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(_description));
        }
    }
}