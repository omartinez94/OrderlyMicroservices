using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class WalkInQueueEndpointTests(CatalogWebApplicationFactory factory)
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
    public async Task AddToWalkInQueue_Returns201()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { customerName = "Jane Doe", partySize = 3 };
        
        var response = await client.PostAsJsonAsync($"/api/v1/restaurants/{restaurantId}/walk-in-queue", request);
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWalkInQueue_Returns200()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        
        var response = await client.GetAsync($"/api/v1/restaurants/{restaurantId}/walk-in-queue");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NotifyWalkInCustomer_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();
        
        var response = await client.PutAsync($"/api/v1/walk-in-queue/{id}/notify", null);
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SeatWalkInCustomer_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();
        
        var response = await client.PutAsync($"/api/v1/walk-in-queue/{id}/seat", null);
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveFromQueue_Returns204()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();
        
        var response = await client.DeleteAsync($"/api/v1/walk-in-queue/{id}");
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
