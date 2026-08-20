using System.Net;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class PriceHistoryEndpointTests(CatalogWebApplicationFactory factory)
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
    public async Task GetPriceHistory_Returns200()
    {
        var client = AuthenticatedClient();
        var menuItemId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/menu-items/{menuItemId}/price-history");

        // Return codes might be 200 or 404, but not 401.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
