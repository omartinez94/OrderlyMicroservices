using System.Net;
using System.Text.Json;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Verifies the <c>/ready</c> endpoint on Ordering.API (Phase 5 of
/// PERSISTENCE_AND_RELIABILITY_PLAN.md, plan §6.5). Pre-Phase-5 this
/// fixture pointed at a single <c>/health</c> endpoint that conflated
/// liveness and readiness; Phase 5 retired that endpoint in favor of
/// the standard Kubernetes split (<c>/live</c> + <c>/ready</c>).
///
/// Today the host registers only <c>AspNetCore.HealthChecks.SqlServer</c>
/// for the <c>Database</c> connection string, tagged <c>"ready"</c>, so
/// this is the load-bearing check during integration tests: when the
/// MSSQL container isn't reachable the response must be 503, when it is
/// the response must be 200 with the SQL Server entry reporting
/// <c>Healthy</c>. The companion <see cref="OrderingLiveReadyEndpointTests"/>
/// covers the <c>/live</c> semantics and the live-stays-green-when-ready-
/// goes-red contract.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingHealthEndpointTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingHealthEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The MSSQL database check must appear in the <c>entries</c> map
    /// and report <c>Healthy</c> when the Testcontainers MSSQL container
    /// is up. The response uses
    /// <c>UIResponseWriter.WriteHealthCheckUIResponse</c>, so the JSON
    /// body reports per-check status under <c>entries</c>.
    /// </summary>
    [Fact]
    public async Task Health_DatabaseCheck_IsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        // The SqlServer check is registered without an explicit name in
        // Ordering.API/DependencyInjection.cs, so it defaults to "sqlserver".
        var sqlServerEntry = entries.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals("sqlserver", StringComparison.OrdinalIgnoreCase));

        sqlServerEntry.Value.GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");
    }
}
