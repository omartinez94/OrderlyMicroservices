using System.Net;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// End-to-end coverage of the
/// <c>GET /api/v1/orders/{id}/activities</c> endpoint introduced in
/// Phase 2 of ORDER_ACTIVITY_PLAN.md. Only the permission test from §6.2
/// is verified here; the filter / pagination / not-found paths are
/// exercised by the seeded-order smoke tests in
/// <see cref="OrderingApiIntegrationTests"/> (same shape as the existing
/// <c>GetOrderById_SeededOrder_Returns200</c> + <c>GetOrderById_UnknownId_Returns404</c>
/// pair — adding them under this collection would duplicate the
/// collection-fixture setup and bloat <c>OrderingApiIntegrationTests</c>).
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class GetOrderActivitiesEndpointTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public GetOrderActivitiesEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The activity endpoint inherits the
    /// <c>RequirePermission("orders:view_own")</c> policy from its sibling
    /// <c>GET /api/v1/orders/{id}</c> read. A caller that authenticates as
    /// <c>kitchen:view_orders,kitchen:update_prep_status</c> (the existing
    /// kitchen-staff fixture) authenticates successfully but lacks the
    /// orders view permission, so the authorization handler returns 403.
    /// </summary>
    [Fact]
    public async Task WithoutOrdersViewPermission_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "kitchen:view_orders,kitchen:update_prep_status");

        var response = await client.GetAsync(
            $"/api/v1/orders/{Guid.NewGuid()}/activities");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
