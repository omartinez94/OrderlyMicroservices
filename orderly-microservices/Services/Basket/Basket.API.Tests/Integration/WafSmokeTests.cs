namespace Basket.API.Tests.Integration;

/// <summary>
/// Smoke test that the <see cref="BasketWebApplicationFactory"/> boots
/// the full Basket host (Postgres + Redis + RabbitMQ containers +
/// Carter + MediatR + Marten + MassTransit + outbox + rate limiter +
/// idempotency filter + health checks + OpenTelemetry) without a
/// runtime DI error and that the test auth scheme resolves an
/// authenticated principal.
/// </summary>
/// <remarks>
/// <para>This is the canary test — if the WAF fails to start (most
/// commonly because <c>ICurrentRestaurantProvider</c> is not
/// registered, or because the Discount gRPC client cannot resolve
/// its address), the other 30 endpoint tests cannot run. Keep this
/// test in the first alphabetical position so the failure message
/// is the first thing a contributor sees when the WAF is broken.</para>
/// <para>Hits the <c>/live</c> endpoint (no auth required) plus a
/// <c>/api/v1/cart</c> call with the test user header to prove the
/// auth scheme produces an authenticated principal. The
/// <c>/api/v1/cart</c> response can be 200 (empty cart) or 403
/// (cross-tenant spoofing); both prove the pipeline reached the
/// endpoint. The strict assertion is that the response is NOT
/// 500 — a 500 indicates a WAF misconfiguration.</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class WafSmokeTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task Waf_BootsAndRespondsToLiveProbe()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var liveResponse = await client.GetAsync("/live");

        // Assert
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the WAF must boot the full host pipeline and answer /live");
    }

    [Fact]
    public async Task Waf_TestAuth_ProducesAuthenticatedPrincipal()
    {
        // Arrange
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cart");
        request.Headers.Add("X-Test-User", userId.ToString());

        // Act
        var response = await client.SendAsync(request);

        // Assert — the response is either 200 (empty cart with the
        // matching tenant) or 403 (cross-tenant — should NOT happen
        // because the test user is from the same tenant the
        // TestAuthHandler stamps). The strict check is that the
        // pipeline reached the endpoint and did not 500.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden);
    }
}
