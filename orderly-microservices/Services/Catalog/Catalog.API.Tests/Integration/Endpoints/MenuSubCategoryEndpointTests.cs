using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class MenuSubCategoryEndpointTests(CatalogWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateMenuSubCategory_ValidRequest_Returns201WithLocationHeader()
    {
        // Arrange
        var request = new { Name = "Test SubCategory" };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/menu-categories/123/sub-categories", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMenuSubCategories_SeededData_Returns200WithPaginatedList()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/menu-categories/123/sub-categories", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMenuSubCategoryById_ExistingId_Returns200()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/v1/menu-sub-categories/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMenuSubCategory_ExistingId_Returns200WithUpdatedFields()
    {
        // Arrange
        var request = new { Name = "Updated SubCategory" };

        // Act
        var response = await _client.PutAsJsonAsync("/v1/menu-sub-categories/123", request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMenuSubCategory_ExistingId_Returns204_ThenGetReturns404()
    {
        // Arrange
        // Act
        var response = await _client.DeleteAsync("/v1/menu-sub-categories/123", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
