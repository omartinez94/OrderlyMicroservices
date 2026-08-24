using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using System.Net.Http.Headers;

namespace Catalog.API.Tests.Integration.Endpoints;

public class MenuCategoryEndpointTests : IClassFixture<OrderlyTestFactory<Program>>
{
    private readonly OrderlyTestFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public MenuCategoryEndpointTests(OrderlyTestFactory<Program> factory)
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
    public async Task CreateMenuCategory_ValidRequest_Returns201WithLocationHeader()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var request = new { Name = "Category" };
        var response = await _client.PostAsJsonAsync($"/v1/restaurants/{rId}/menu-categories", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMenuCategories_SeededData_Returns200WithPaginatedList()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        await TestDataSeeder.SeedMenuCategoryAsync(_factory, rId);
        var response = await _client.GetAsync($"/v1/restaurants/{rId}/menu-categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMenuCategoryById_ExistingId_Returns200()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuCategoryAsync(_factory, rId);
        var response = await _client.GetAsync($"/v1/menu-categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMenuCategory_ExistingId_Returns200WithUpdatedFields()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuCategoryAsync(_factory, rId);
        var request = new { Name = "Updated Category" };
        var response = await _client.PutAsJsonAsync($"/v1/menu-categories/{id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMenuCategory_ExistingId_Returns204_ThenGetReturns404()
    {
        await AuthenticateAsync();
        var rId = await TestDataSeeder.SeedRestaurantAsync(_factory);
        var id = await TestDataSeeder.SeedMenuCategoryAsync(_factory, rId);
        var deleteResponse = await _client.DeleteAsync($"/v1/menu-categories/{id}");
        var getResponse = await _client.GetAsync($"/v1/menu-categories/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
