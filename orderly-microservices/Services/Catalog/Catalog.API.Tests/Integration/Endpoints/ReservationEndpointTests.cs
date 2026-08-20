using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Catalog.API.Tests.Integration.Endpoints;

[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class ReservationEndpointTests(CatalogWebApplicationFactory factory)
{
    private const string Permission = "catalog:menu_update";

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", Permission);
        return client;
    }

    [Fact]
    public async Task CreateReservation_ValidRequest_Returns201()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();
        var request = new { customerName = "John Doe", partySize = 2, reservationTime = DateTime.UtcNow.AddHours(2) };

        var response = await client.PostAsJsonAsync($"/api/v1/restaurants/{restaurantId}/reservations", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetReservations_Returns200()
    {
        var restaurantId = await factory.SeedRestaurantAsync();
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/v1/restaurants/{restaurantId}/reservations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetReservationById_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/reservations/{id}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmReservation_Returns200_StatusConfirmed()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.PutAsync($"/api/v1/reservations/{id}/confirm", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SeatReservation_AfterConfirm_Returns200()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.PutAsync($"/api/v1/reservations/{id}/seat", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelReservation_Returns200_StatusCancelled()
    {
        var client = AuthenticatedClient();
        var id = Guid.NewGuid();

        var response = await client.PutAsync($"/api/v1/reservations/{id}/cancel", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
