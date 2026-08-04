using System.Net;
using System.Text.Json;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// Phase 5 of PERSISTENCE_AND_RELIABILITY_PLAN.md (plan §6.5).
///
/// Verifies the in-box <c>Microsoft.AspNetCore.OpenApi</c> emitter
/// serves a well-formed OpenAPI 3.0 document at
/// <c>/openapi/v1.json</c>. The document must:
/// <list type="bullet">
/// <item>be valid JSON;</item>
/// <item>declare an <c>openapi</c> version of <c>3.0.x</c>;</item>
/// <item>expose at least one <c>path</c> entry;</item>
/// <item>carry the <c>"Orders"</c> tag on at least one operation (proves
/// the existing <c>.WithTags("Orders")</c> wiring on each Carter module's
/// route group is being read by the in-box emitter).</item>
/// </list>
///
/// The smoke + tag-strict checks live in
/// <c>.github/workflows/openapi-smoke.yml</c>; this in-process test
/// exercises the same shape against the existing
/// <see cref="OrderingWebApplicationFactory"/> Testcontainers fixture so
/// CI gets fast feedback even when the workflow runner lacks Docker-in-Docker.
/// </summary>
[Collection(nameof(OrderingWebApplicationFactoryCollection))]
public sealed class OrderingOpenApiEndpointTests
{
    private readonly OrderingWebApplicationFactory _factory;

    public OrderingOpenApiEndpointTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApiDocument_IsValidJson_WithExpectedShape()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // OpenAPI 3.0.x — the in-box emitter ships OpenAPI 3.0 today.
        // Future SDK versions may upgrade to 3.1; the assertion is loose
        // so we don't break on a minor bump.
        var openapiVersion = root.GetProperty("openapi").GetString();
        openapiVersion.Should().StartWith("3.");

        // paths: must be a non-empty object.
        var paths = root.GetProperty("paths");
        paths.ValueKind.Should().Be(JsonValueKind.Object);
        var pathCount = paths.EnumerateObject().Count();
        pathCount.Should().BeGreaterThan(0,
            "the in-box emitter should have enumerated at least one Carter module route");

        // At least one operation under one path carries the "Orders"
        // tag (matches the .WithTags("Orders") wired on every Ordering
        // Carter module's route group).
        var ordersTagged = false;
        foreach (var pathProperty in paths.EnumerateObject())
        {
            foreach (var opProperty in pathProperty.Value.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Object))
            {
                if (opProperty.Value.TryGetProperty("tags", out var tagsElement) &&
                    tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tagsElement.EnumerateArray())
                    {
                        if (tag.ValueKind == JsonValueKind.String &&
                            tag.GetString() == "Orders")
                        {
                            ordersTagged = true;
                            break;
                        }
                    }
                }
                if (ordersTagged)
                {
                    break;
                }
            }
            if (ordersTagged)
            {
                break;
            }
        }

        ordersTagged.Should().BeTrue(
            "expected at least one operation tagged 'Orders' — the Carter modules wire .WithTags(\"Orders\") on every route group");
    }
}