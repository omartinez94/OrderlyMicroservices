namespace Basket.API.Endpoints;

/// <summary>
/// Centralised route-group configuration for every Basket endpoint.
/// Replaces the per-module <c>MapGroup("/api/v1")</c> calls so the
/// default authorization policy, OpenAPI opt-in, and tag name live in
/// one place. New endpoint modules call <c>app.MapBasketGroup()</c>
/// instead of <c>app.MapGroup("/api/v1")</c>; per-route
/// <c>RequirePermission(...)</c> calls stay on the route definition.
/// </summary>
public static class BasketEndpointGroup
{
    /// <summary>
    /// Maps <c>/api/v1</c> with <c>RequireAuthorization("Default")</c>
    /// and the <c>Baskets</c> tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WithOpenApi()</c> is **temporarily disabled**. The current
    /// <c>Microsoft.OpenApi 2.7.5</c> package (a major rewrite of the
    /// 1.x line) relocated <c>OpenApiOperation</c> out of the
    /// <c>Microsoft.OpenApi.Models</c> namespace. ASP.NET Core's
    /// <c>WithOpenApi()</c> extension from <c>Microsoft.AspNetCore.OpenApi</c>
    /// still looks for the type in the v1.x namespace, so calling
    /// it throws <c>TypeLoadException</c> on the first request to
    /// any cart endpoint — the route never resolves. The
    /// plan drift item 2 documented this; the proper fix is either
    /// (a) downgrade <c>Microsoft.OpenApi</c> to a 1.x line or
    /// (b) replace <c>WithOpenApi()</c> with a Swashbuckle-native
    /// mapping that knows the v2.x namespace. Both are out of
    /// scope; tracked as a follow-up alongside
    /// the OpenAPI mirror test.
    /// </para>
    /// <para>
    /// Until that fix lands, the Swashbuckle <c>AddSwaggerGen</c>
    /// generator still enumerates every endpoint registered with
    /// <c>MapBasketGroup()</c> via the
    /// <c>AddEndpointsApiExplorer</c> discovery, so the JSON spec
    /// served at <c>/swagger/v1/swagger.json</c> still lists every
    /// route — the generator just cannot attach the
    /// v1.x-typed <c>OpenApiOperation</c> metadata.
    /// </para>
    /// </remarks>
    public static RouteGroupBuilder MapBasketGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/v1")
            .RequireAuthorization("Default")
            .WithTags("Baskets");
    // WithOpenApi() DISABLED — see xmldoc above for the
    // Microsoft.OpenApi 2.7.5 namespace incompatibility. Re-enable
    // when the v2.x type mapping is fixed.
}