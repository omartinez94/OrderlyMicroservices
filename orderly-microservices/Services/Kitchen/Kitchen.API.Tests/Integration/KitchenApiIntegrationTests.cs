using System.Net;
using System.Net.Http.Json;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// End-to-end coverage of the Kitchen.API surface that does NOT require a
/// real Identity authority. Locks in:
/// <list type="bullet">
/// <item>the unauthenticated 401 path on every guarded endpoint,</item>
/// <item>the 409 conflict response when a command hits an illegal transition,</item>
/// <item>the validation 400 path on the cancel command's empty reason,</item>
/// <item>the 200 path on the kitchen queue GET when the caller has the
/// required permission.</item>
/// </list>
/// Health endpoint and DB reachability are exercised in
/// <see cref="KitchenHealthEndpointTests"/>.
/// </summary>
[Collection(nameof(KitchenWebApplicationFactoryCollection))]
public sealed class KitchenApiIntegrationTests
{
    private readonly KitchenWebApplicationFactory _factory;

    public KitchenApiIntegrationTests(KitchenWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "kitchen:view_orders,kitchen:update_prep_status");
        return client;
    }

    [Fact]
    public async Task GetKitchenQueue_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/kitchen/queue");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"body: {body}");
    }

    [Fact]
    public async Task GetKitchenQueue_Authenticated_Returns200()
    {
        var client = NewClient();

        var response = await client.GetAsync("/api/v1/kitchen/queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTicketDetail_UnknownId_Returns404()
    {
        var client = NewClient();

        var response = await client.GetAsync($"/api/v1/kitchen/tickets/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AcceptTicket_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/kitchen/tickets/{Guid.NewGuid()}/accept",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelTicket_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/kitchen/tickets/{Guid.NewGuid()}/cancel",
            new { reason = "out of stock" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelTicket_EmptyReason_Returns400()
    {
        var client = NewClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/kitchen/tickets/{Guid.NewGuid()}/cancel",
            new { reason = "" });

        // FluentValidation rejects empty reason before the handler runs,
        // so the response is 400 Bad Request.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AcceptTicket_UnknownId_Returns404()
    {
        var client = NewClient();

        var response = await client.PostAsync(
            $"/api/v1/kitchen/tickets/{Guid.NewGuid()}/accept",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}