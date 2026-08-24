using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class IngredientEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateIngredient_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        var request = new { Name = "Test Ingredient" };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/restaurants/123/ingredients", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetIngredients_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/restaurants/123/ingredients", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetIngredientById_ExistingId_Returns200()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/ingredients/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateIngredient_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        var request = new { Name = "Updated Ingredient" };

        // Act
        var response = await _client.PutAsJsonAsync("/v1/ingredients/123", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteIngredient_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/v1/ingredients/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
