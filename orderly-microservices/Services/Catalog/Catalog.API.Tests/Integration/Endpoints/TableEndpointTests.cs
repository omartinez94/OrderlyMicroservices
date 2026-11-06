using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class TableEndpointTests(CatalogWebApplicationFactory factory)
{
    private const string Permission = "menu:edit,menu:view";

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
        var request = new
        {
            restaurantId,
            tableNumber = "T1",
            capacity = 4,
            shape = "Round",
            positionX = 0,
            positionY = 0,
        };

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
        var request = new
        {
            restaurantId,
            tableNumber = "T2",
            capacity = 2,
            shape = "Square",
            positionX = 1,
            positionY = 1,
        };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        location.Should().NotBeNull();
        var response = await client.GetAsync(location);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateTable_ExistingId_Returns200WithUpdatedFields()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new
        {
            restaurantId,
            tableNumber = "T3",
            capacity = 2,
            shape = "Square",
            positionX = 2,
            positionY = 2,
        };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        location.Should().NotBeNull();
        var updateRequest = new
        {
            tableNumber = "T3-Updated",
            capacity = 4,
            shape = "Square",
            positionX = 2,
            positionY = 2,
            status = 0,
        };
        var response = await client.PutAsJsonAsync(location, updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteTable_ExistingId_Returns200_ThenGetReturns404()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new
        {
            restaurantId,
            tableNumber = "T4",
            capacity = 2,
            shape = "Square",
            positionX = 3,
            positionY = 3,
        };
        var createResponse = await client.PostAsJsonAsync($"/v1/restaurants/{restaurantId}/tables", request);
        var location = createResponse.Headers.Location;

        location.Should().NotBeNull();
        var deleteResponse = await client.DeleteAsync(location);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync(location);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
