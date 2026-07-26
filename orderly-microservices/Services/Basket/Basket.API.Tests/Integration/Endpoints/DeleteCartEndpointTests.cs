namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// End-to-end HTTP contract tests for <c>DELETE /api/v1/cart</c>.
/// Locks the §0.4.3 (204 No Content) + §0.4.7 (idempotent delete:
/// deleting a non-existent cart also returns 204, never 404)
/// contracts over the real ASP.NET Core pipeline.
/// </summary>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class DeleteCartEndpointTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task DeleteCart_ExistingCart_Returns204NoContent()
    {
        // Arrange — seed a cart.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await factory.SeedBasketAsync(userId);

        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        // Act
        var response = await client.DeleteAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "§0.4.3: DELETE /api/v1/cart returns 204 No Content with no body");

        response.Headers.CacheControl.Should().NotBeNull(
            "every cart endpoint must set Cache-Control: no-store (PII)");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();

        // Verify the basket is actually gone — a follow-up GET must
        // return 200 + empty cart (per §0.4.7, never 404).
        using var verify = factory.CreateClient();
        verify.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        verify.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:view_own");
        var followUp = await verify.GetAsync("/api/v1/cart");
        followUp.StatusCode.Should().Be(HttpStatusCode.OK,
            "a deleted cart must project to an empty body, not 404");
    }

    [Fact]
    public async Task DeleteCart_AbsentCart_Returns204NoContent()
    {
        // Arrange — no prior cart for this user.
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "orders:create");

        // Act
        var response = await client.DeleteAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "DELETE is idempotent — deleting a non-existent cart also returns 204 (never 404)");
    }

    [Fact]
    public async Task DeleteCart_AnonymousRequest_Returns401()
    {
        // Arrange — no X-Test-User header.
        using var client = factory.CreateClient();

        // Act
        var response = await client.DeleteAsync("/api/v1/cart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "no authenticated principal => 'Default' policy returns 401");
    }
}
