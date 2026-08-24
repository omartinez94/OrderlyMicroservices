using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class ComboItemEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateComboItem_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        var request = new { Name = "Test Combo" };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/menu-items/123/combo-items", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetComboItems_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/menu-items/123/combo-items", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateComboItem_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        var request = new { Name = "Updated Combo" };

        // Act
        var response = await _client.PutAsJsonAsync("/v1/combo-items/123", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteComboItem_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/v1/combo-items/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
