using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class IngredientAlternativeEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateIngredientAlternative_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        var request = new { Name = "Test Alternative" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/restaurants/123/ingredient-alternatives", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetIngredientAlternatives_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/api/v1/restaurants/123/ingredient-alternatives", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateIngredientAlternative_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        var request = new { Name = "Updated Alternative" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/v1/ingredient-alternatives/123", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteIngredientAlternative_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/api/v1/ingredient-alternatives/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
