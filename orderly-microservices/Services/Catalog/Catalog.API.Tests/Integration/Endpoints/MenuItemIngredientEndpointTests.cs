using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class MenuItemIngredientEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AddMenuItemIngredient_ValidRequest_Returns201()
    {
        // Arrange
        var request = new { IngredientId = "123" };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/menu-items/123/ingredients", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetMenuItemIngredients_Returns200WithList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/menu-items/123/ingredients", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveMenuItemIngredient_Returns204()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/v1/menu-items/123/ingredients/456", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
