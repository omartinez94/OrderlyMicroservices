using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// Guards the endpoint graph against parameter-binding faults that are
/// invisible at compile time and fatal at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Four Catalog endpoints shipped with a NodaTime type
/// (<c>LocalDate</c>/<c>Instant</c>) as a property of an
/// <c>[AsParameters]</c> request record on a GET route. Minimal API binds such
/// a property from the query string only when its type is parsable
/// (<c>IParsable&lt;T&gt;</c> or a static <c>TryParse</c>); NodaTime's types are
/// neither, so the binder inferred them as a <i>body</i> parameter, which is
/// illegal on GET.
/// </para>
/// <para>
/// The consequence was disproportionate to the mistake. The exception is
/// raised while <c>CompositeEndpointDataSource</c> materialises the endpoint
/// list — so it took down <b>every route in the service</b>, including
/// <c>/live</c>, and the container sat in an unhealthy loop returning 500 to
/// all traffic. Nothing failed at build time and no test caught it; the first
/// signal was a dead service.
/// </para>
/// <para>
/// Enumerating the data source forces exactly that materialisation, turning a
/// production outage into a red test.
/// </para>
/// </remarks>
[Collection(nameof(CatalogWebApplicationFactoryCollection))]
public sealed class CatalogEndpointGraphTests(CatalogWebApplicationFactory factory)
{
    [Fact]
    public void EveryEndpoint_Materialises_WithoutBindingFailure()
    {
        // Triggers host build (idempotent across the collection).
        _ = factory.CreateClient();

        var dataSources = factory.Services.GetServices<EndpointDataSource>().ToList();
        dataSources.Should().NotBeEmpty("the host must register at least one endpoint data source");

        // The act itself is the assertion: materialising the endpoint list is
        // the code path that threw InvalidOperationException("Body was
        // inferred but the method does not allow inferred body parameters").
        var materialise = () => dataSources.SelectMany(source => source.Endpoints).ToList();

        materialise.Should().NotThrow(
            "every endpoint must bind its parameters from route/query/services — a GET route " +
            "with an unparsable [AsParameters] property (e.g. a NodaTime LocalDate or Instant) " +
            "fails the whole endpoint graph, not just its own route");

        materialise().Should().HaveCountGreaterThan(
            1,
            "a near-empty graph would let this test pass vacuously");
    }

    [Fact]
    public async Task LivenessProbe_Responds_WhenEndpointGraphIsIntact()
    {
        // The end-to-end symptom of the binding fault, asserted directly: a
        // broken graph makes *every* request 500, so the container's /live
        // probe fails and Docker reports it unhealthy even though the process
        // is running. This is the check that actually went red in production.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/live");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.InternalServerError,
            "a 500 from /live means the endpoint graph failed to build");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
