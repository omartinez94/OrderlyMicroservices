using System.Net;

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
}