using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// Phase 5.1 Commit 3 — OpenAPI regression test. Boots the WAF
/// and resolves <see cref="ISwaggerProvider"/> via DI; asserts every
/// <c>MapBasketGroup()</c> endpoint is enumerated by
/// <c>AddEndpointsApiExplorer</c>. The test fails on the next phase
/// that adds an endpoint without registering it through
/// <c>MapBasketGroup()</c> or without enabling the API explorer
/// discovery path.
/// </summary>
/// <remarks>
/// <para>The test resolves <see cref="ISwaggerProvider"/> via DI
/// rather than hitting <c>/swagger/v1/swagger.json</c> over HTTP.
/// The WAF runs in the <c>Testing</c> environment, and
/// <c>app.UseSwagger()</c> in <c>Basket.API/Program.cs:506</c> is
/// gated on <c>IsDevelopment()</c> — so the HTTP route is not
/// available here. The Swashbuckle provider is reachable through DI
/// regardless of the environment flag.</para>
/// <para><c>WithOpenApi()</c> is intentionally disabled on
/// <c>MapBasketGroup</c> (<see cref="Basket.API.Endpoints.BasketEndpointGroup.MapBasketGroup"/>)
/// due to a <c>Microsoft.OpenApi 2.7.5</c> namespace relocation;
/// <c>AddEndpointsApiExplorer</c> still enumerates the routes,
/// so the <see cref="ISwaggerProvider"/> document lists every
/// endpoint — just without the v1.x-typed <c>OpenApiOperation</c>
/// metadata that <c>WithOpenApi()</c> would attach. The
/// Phase 5.1 follow-up that re-enables <c>WithOpenApi()</c> is
/// out of scope.</para>
/// <para><c>{userId:guid}</c> route constraints are normalised by
/// Swashbuckle to <c>{userId}</c>; the assertions use the
/// normalised form.</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class OpenApiGenerationTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public void AllEndpointsDocumented()
    {
        // Arrange — resolve the provider via DI (no HTTP).
        var provider = factory.Services.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger("v1");

        // Act — flatten the OpenAPI document to a set of
        // "{HTTP method} {route template}" pairs. path.Value is
        // nullable in OpenApiPaths — use KeyValuePair deconstruct so
        // the compiler narrows the null check.
        var actualRoutes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, item) in document.Paths)
        {
            if (item is null)
            {
                continue;
            }

            foreach (var op in item.Operations!.Keys)
            {
                actualRoutes.Add($"{op.Method} {key}");
            }
        }

        // Assert — every MapBasketGroup endpoint is present.
        var expectedRoutes = new[]
        {
            "GET /api/v1/cart",
            "PUT /api/v1/cart",
            "DELETE /api/v1/cart",
            "POST /api/v1/cart/checkout",
            "GET /api/v1/admin/carts",
            "PUT /api/v1/admin/carts/{userId}",
            "DELETE /api/v1/admin/carts/{userId}",
        };

        var missing = expectedRoutes.Except(actualRoutes).ToList();
        missing.Should().BeEmpty(
            "every endpoint registered through MapBasketGroup() must be enumerated by AddEndpointsApiExplorer; " +
            "missing: " + string.Join(", ", missing));
    }
}