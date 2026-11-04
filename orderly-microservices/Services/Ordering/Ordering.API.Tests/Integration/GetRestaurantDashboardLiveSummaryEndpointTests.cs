namespace Ordering.API.Tests.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Ordering.Application.Dashboard.Dtos;

[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class GetRestaurantDashboardLiveSummaryEndpointTests(OrderingWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    private HttpClient CreateClient(Guid? restaurantId = null, string? permissions = null, string? role = null)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        if (permissions is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Permissions", permissions);
        }
        if (restaurantId.HasValue)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Restaurant", restaurantId.Value.ToString());
        }
        if (role is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Role", role);
        }
        return client;
    }

    [Fact]
    public async Task GetDashboard_NoAuth_Returns401()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{Guid.NewGuid()}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_WithoutDashboardViewPermission_Returns403()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(restaurantId, permissions: "orders:view_own");
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{restaurantId}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDashboard_WithMismatchedRestaurantId_Returns403()
    {
        Guid userRestaurantId = Guid.NewGuid();
        Guid targetRestaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(userRestaurantId, permissions: "dashboard:view");
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{targetRestaurantId}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDashboard_WithSuperAdminRole_MatchesAnyRestaurant()
    {
        Guid targetRestaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(Guid.NewGuid(), permissions: "dashboard:view", role: "SuperAdmin");
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{targetRestaurantId}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_WithDashboardView_WithoutPaymentsViewReports_RedactsFinancials()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(restaurantId, permissions: "dashboard:view");
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{restaurantId}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RestaurantOperationsDashboardDto? dto = await response.Content.ReadFromJsonAsync<RestaurantOperationsDashboardDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.RestaurantId.Should().Be(restaurantId);

        // Revenue fields must be redacted
        dto.Ticker.NetSalesToday.Should().BeNull();
        dto.Ticker.SalesTargetToday.Should().BeNull();
        dto.Ticker.SalesGrowthPercentage.Should().BeNull();
        dto.SalesPace.OpenBalanceTotal.Should().BeNull();
        dto.SalesPace.CashTenderedTotal.Should().BeNull();
        dto.SalesPace.DigitalTenderedTotal.Should().BeNull();

        // Operational non-financial metrics must remain intact
        dto.Ticker.ActiveOrdersCount.Should().BeGreaterThanOrEqualTo(0);
        dto.Meta.Sections.Should().ContainKey("kitchenVelocity");
        dto.Meta.Sections.Should().ContainKey("floorPulse");
    }

    [Fact]
    public async Task GetDashboard_WithPaymentsViewReports_IncludesFinancials()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient client = CreateClient(restaurantId, permissions: "dashboard:view,payments:view_reports");
        HttpResponseMessage response = await client.GetAsync($"/v1/restaurants/{restaurantId}/dashboard/live-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RestaurantOperationsDashboardDto? dto = await response.Content.ReadFromJsonAsync<RestaurantOperationsDashboardDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.RestaurantId.Should().Be(restaurantId);

        // Revenue fields must NOT be null
        dto.Ticker.NetSalesToday.Should().NotBeNull();
        dto.Ticker.SalesGrowthPercentage.Should().NotBeNull();
        dto.SalesPace.OpenBalanceTotal.Should().NotBeNull();
    }
}
