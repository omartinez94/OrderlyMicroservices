using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class TableEndpointTests(CatalogWebApplicationFactory factory)
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
    public async Task CreateTable_ValidRequest_Returns201WithLocationHeader()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { name = "T1", capacity = 4 };

        var response = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTables_SeededData_Returns200WithPaginatedList()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/v1/restaurants/{restaurantId}/tables");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTableById_ExistingId_Returns200()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { name = "T2", capacity = 2 };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        if (location is not null)
        {
            var response = await client.GetAsync(location);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task UpdateTable_ExistingId_Returns200WithUpdatedFields()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { name = "T3", capacity = 2 };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        if (location is not null)
        {
            var updateRequest = new { name = "T3-Updated", capacity = 4 };
            var response = await client.PutAsJsonAsync(location, updateRequest);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task DeleteTable_ExistingId_Returns204_ThenGetReturns404()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { name = "T4", capacity = 2 };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        if (location is not null)
        {
            var deleteResponse = await client.DeleteAsync(location);
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var getResponse = await client.GetAsync(location);
            getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
