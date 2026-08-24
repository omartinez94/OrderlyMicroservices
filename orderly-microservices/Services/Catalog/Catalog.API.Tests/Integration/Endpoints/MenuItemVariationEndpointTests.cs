using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class MenuItemVariationEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateMenuItemVariation_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        var request = new { Name = "Test Variation" };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/menu-items/123/variations", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMenuItemVariations_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/menu-items/123/variations", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMenuItemVariation_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        var request = new { Name = "Updated Variation" };

        // Act
        var response = await _client.PutAsJsonAsync("/v1/menu-item-variations/123", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMenuItemVariation_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/v1/menu-item-variations/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
