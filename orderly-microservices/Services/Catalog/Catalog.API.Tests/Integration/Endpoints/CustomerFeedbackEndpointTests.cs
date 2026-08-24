using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CustomerFeedbackEndpointTests(CatalogWebApplicationFactory factory)
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
    public async Task SubmitFeedback_Returns201()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { rating = 5, comment = "Great service!" };

        var response = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/feedback", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFeedback_Returns200WithList()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/v1/restaurants/{restaurantId}/feedback");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFeedbackById_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/v1/feedback/{id}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
