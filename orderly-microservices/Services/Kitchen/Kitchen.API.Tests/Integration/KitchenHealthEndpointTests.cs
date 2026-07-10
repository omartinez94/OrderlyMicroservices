using System.Net;
using System.Text.Json;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// Verifies the <c>/health</c> endpoint reports both the EF Core context
/// and the RabbitMQ broker as healthy when the host has its real Postgres
/// + RabbitMQ containers wired up via Testcontainers. The response uses
/// <c>UIResponseWriter.WriteHealthCheckUIResponse</c> so the JSON body
/// reports per-check status under the <c>entries</c> map.
/// </summary>
[Collection(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class KitchenHealthEndpointTests
{
    private readonly KitchenWebApplicationFactory _factory;

    public KitchenHealthEndpointTests(KitchenWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The broker check is present in the entries map
    /// and reports <c>Healthy</c> when the RabbitMQ container is up.
    /// </summary>
    [Fact]
    public async Task Health_BrokerCheck_IsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        var broker = entries.GetProperty("messagebroker");
        broker.GetProperty("status").GetString().Should().Be("Healthy");
        broker.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString()).Should().Contain(new[] { "broker", "ready" });
    }

    /// <summary>
    /// once the RabbitMQ container is stopped, <c>/health</c> must
    /// flip to 503 with <c>entries.messagebroker.status == Unhealthy</c>.
    /// Stops the RabbitMQ container in place so the EF check still passes.
    /// </summary>
    [Fact]
    public async Task Health_WhenBrokerDown_Returns503WithBrokerUnhealthy()
    {
        // Sanity check: /health is 200 + Healthy before we tear anything down.
        var client = _factory.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Stop the broker. The Testcontainers RabbitMQ container is the
        // back-end of the AspNetCore.HealthChecks.Rabbitmq check, so
        // dismounting it makes the next probe fail.
        await _factory.StopRabbitMqContainerAsync();

        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var broker = doc.RootElement.GetProperty("entries").GetProperty("messagebroker");
        broker.GetProperty("status").GetString().Should().Be("Unhealthy");
    }
}
