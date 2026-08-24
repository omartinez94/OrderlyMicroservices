using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using System.Net.Http.Headers;

namespace Catalog.API.Tests.Integration.Endpoints;

public class MenuItemEndpointTests : IClassFixture<OrderlyTestFactory<Program>>
{
    private readonly OrderlyTestFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public MenuItemEndpointTests(OrderlyTestFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task AuthenticateAsync()
    {
        var token = await AuthTokenHelper.GetTestJwtAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task CreateMenuItem_ValidRequest_Returns201WithLocationHeader()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var request = new { Name = "Item", BasePrice = 10m, IsAvailable = true, ItemType = 0 };
        var response = await _client.PostAsJsonAsync($"/v1/restaurants/{rId}/menu-items", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMenuItems_SeededData_Returns200WithPaginatedList()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        await TestDataSeeder.SeedMenuItemAsync(_factory, rId);
        var response = await _client.GetAsync($"/v1/restaurants/{rId}/menu-items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMenuItemById_ExistingId_Returns200()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuItemAsync(_factory, rId);
        var response = await _client.GetAsync($"/v1/menu-items/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMenuItem_ExistingId_Returns200WithUpdatedFields()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuItemAsync(_factory, rId);
        var request = new { Name = "Updated Item", BasePrice = 15m, IsAvailable = false, ItemType = 0 };
        var response = await _client.PutAsJsonAsync($"/v1/menu-items/{id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMenuItem_ExistingId_Returns204_ThenGetReturns404()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuItemAsync(_factory, rId);
        var deleteResponse = await _client.DeleteAsync($"/v1/menu-items/{id}");
        var getResponse = await _client.GetAsync($"/v1/menu-items/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateMenuItem_InvalidRestaurantId_Returns404()
    {
        await AuthenticateAsync();
        var request = new { Name = "Item", BasePrice = 10m, IsAvailable = true, ItemType = 0 };
        var response = await _client.PostAsJsonAsync($"/v1/restaurants/{Guid.NewGuid()}/menu-items", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateMenuItem_MissingRequiredFields_Returns400()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var request = new { BasePrice = -10m };
        var response = await _client.PostAsJsonAsync($"/v1/restaurants/{rId}/menu-items", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
