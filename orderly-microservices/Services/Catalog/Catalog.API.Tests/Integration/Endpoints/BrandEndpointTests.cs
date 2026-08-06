using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using System.Net.Http.Headers;

namespace Catalog.API.Tests.Integration.Endpoints;

public class BrandEndpointTests : IClassFixture<OrderlyTestFactory<Program>>
{
    private readonly OrderlyTestFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public BrandEndpointTests(OrderlyTestFactory<Program> factory)
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
    public async Task CreateBrand_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        await AuthenticateAsync();
        var request = new { Name = "New Brand", Description = "Test" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/brands", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBrands_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        await AuthenticateAsync();
        await TestDataSeeder.SeedBrandAsync(_factory);

        // Act
        var response = await _client.GetAsync("/api/v1/brands");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBrandById_ExistingId_Returns200()
    {
        // Arrange
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedBrandAsync(_factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/brands/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateBrand_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedBrandAsync(_factory);
        var request = new { Name = "Updated Brand", Description = "Updated" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/brands/{id}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteBrand_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        await AuthenticateAsync();
        var id = await TestDataSeeder.SeedBrandAsync(_factory);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/v1/brands/{id}");
        var getResponse = await _client.GetAsync($"/api/v1/brands/{id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
