using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class MenuItemAnalyticsEndpointTests(CatalogWebApplicationFactory factory)
{
    private const string Permission = "catalog:menu_update";

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", Permission);
        return client;
    }

    [Fact]
    public async Task GetAnalytics_Returns200()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/v1/restaurants/{restaurantId}/analytics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAnalyticsById_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/v1/analytics/{id}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecomputeTodayAnalytics_Returns200()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();

        var response = await client.PostAsync($"/v1/restaurants/{restaurantId}/analytics/recompute", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
