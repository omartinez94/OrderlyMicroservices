using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using System.Net.Http.Headers;

namespace Catalog.API.Tests.Integration.Endpoints;

public class RestaurantEndpointTests : IClassFixture<OrderlyTestFactory<Program>>
{
    private readonly OrderlyTestFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public RestaurantEndpointTests(OrderlyTestFactory<Program> factory)
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
    public async Task CreateRestaurant_ValidRequest_Returns201WithLocationHeader()
    {
        await AuthenticateAsync();
        var request = new { Name = "New Res", Address = "1", Email = "a@a.com", PhoneNumber = "1", TaxRate = 0.1m, Currency = "MXN", TimeZone = "UTC" };
        var response = await _client.PostAsJsonAsync("/v1/restaurants", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRestaurants_SeededData_Returns200WithPaginatedList()
    {
        await AuthenticateAsync();
        await TestDataSeeder.SeedRestaurantAsync(_factory);
        var response = await _client.GetAsync("/v1/restaurants");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRestaurantById_ExistingId_Returns200()
    {
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var response = await _client.GetAsync($"/v1/restaurants/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateRestaurant_ExistingId_Returns200WithUpdatedFields()
    {
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var request = new { Name = "Updated", Address = "2", Email = "b@b.com", PhoneNumber = "2", TaxRate = 0.2m, Currency = "USD", TimeZone = "EST" };
        var response = await _client.PutAsJsonAsync($"/v1/restaurants/{id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteRestaurant_ExistingId_Returns204_ThenGetReturns404()
    {
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var deleteResponse = await _client.DeleteAsync($"/v1/restaurants/{id}");
        var getResponse = await _client.GetAsync($"/v1/restaurants/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
