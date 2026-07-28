using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Basket.API.Tests.Integration.Endpoints;

/// <summary>
/// Phase 5.1 Commit 4 — Verify snapshot of the Basket API's
/// OpenAPI surface. The first snapshot in the Orderly repo.
/// Locks the generated <c>docs/api/basket-api-v1.json</c>
/// contract via the same <see cref="ISwaggerProvider"/> the
/// artifact-generation script uses (single source of truth for the
/// API surface).
/// </summary>
/// <remarks>
/// <para><b>Verify.Xunit attribute.</b> Verify.Xunit 28.6.0
/// auto-emits an assembly-level
/// <c>[assembly: VerifyXunit.UseVerifyAttribute()]</c> via the
/// <c>Verify.Xunit.props</c> MSBuild target
/// (visible at <c>obj/Debug/net10.0/VerifyXunit.Attributes.cs</c>),
/// so the class-level <c>[UsesVerify]</c> from older docs is no
/// longer required.</para>
/// <para><b>Microsoft.OpenApi 2.7.5 serialisation.</b> The v2.x
/// package only exposes async serialisers
/// (<c>SerializeAsJsonAsync(stream, OpenApiSpecVersion, ct)</c>);
/// no <c>OpenApiFormat</c> enum and no sync overload. The test
/// serialises to a <see cref="MemoryStream"/>, decodes UTF-8 bytes
/// back to a string, and snapshots that string.</para>
/// <para><b>Why one snapshot of the OpenAPI document, not one
/// per endpoint.</b> The 21 existing integration endpoint tests
/// (<c>GetCartEndpointTests</c>, <c>UpsertCartEndpointTests</c>,
/// <c>DeleteCartEndpointTests</c>, <c>CheckoutCartEndpointTests</c>)
/// already lock the per-(verb, URL, status) contract. Capturing
/// per-endpoint body snapshots would duplicate them with
/// maintenance noise. The OpenAPI document is the reviewable
/// artifact — a future breaking change (a renamed field, a
/// changed status code, a narrowed error envelope) surfaces here
/// as a single diff to review.</para>
/// <para><b>Snapshot location.</b> Verify.Xunit co-locates the
/// <c>.verified.txt</c> file next to the test class. The
/// repository's <c>.gitignore</c> ignores <c>*.received.*</c>
/// (Approval Tests); Verify's <c>.verified.txt</c> is tracked.
/// First run produces <c>.received.txt</c>; review the diff,
/// promote the file to <c>.verified.txt</c>, commit.</para>
/// <para><b>Re-run after schema changes.</b> The
/// <c>scripts/generate-basket-openapi.ps1</c> script regenerates
/// the artifact using the same generator; re-running the snapshot
/// test after schema changes will fail the same way the
/// <c>OpenApiGenerationTests.AllEndpointsDocumented</c> regression
/// test will (added in Commit 3 of this PR). Both tests share the
/// <see cref="ISwaggerProvider"/>; either failing is a signal to
/// update the artifact + the snapshot in lockstep.</para>
/// </remarks>
[Collection(nameof(BasketWebApplicationFactoryCollection))]
public sealed class BasketSnapshotsTests(BasketWebApplicationFactory factory)
{
    [Fact]
    public async Task VerifyAllEndpoints()
    {
        var provider = factory.Services.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger("v1");

        using var ms = new MemoryStream();
        await document.SerializeAsJsonAsync(ms, OpenApiSpecVersion.OpenApi3_0, CancellationToken.None);
        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        await Verifier.Verify(json);
    }
}