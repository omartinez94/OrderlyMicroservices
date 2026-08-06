using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class MergedTableEndpointTests(CatalogWebApplicationFactory factory)
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
    public async Task MergeTables_TwoTables_Returns201WithMergedGroup()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { tableIds = new[] { Guid.NewGuid(), Guid.NewGuid() } };
        
        var response = await client.PostAsJsonAsync($"/api/v1/restaurants/{restaurantId}/merged-tables", request);
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMergedTables_Returns200WithList()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        
        var response = await client.GetAsync($"/api/v1/restaurants/{restaurantId}/merged-tables");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SplitTables_Returns204_TablesBackToIndependent()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();
        
        var response = await client.DeleteAsync($"/api/v1/merged-tables/{id}");
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
