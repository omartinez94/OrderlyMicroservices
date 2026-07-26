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
    /// and the <c>Baskets</c> tag. <c>WithOpenApi()</c> lands in Phase 4
    /// when <c>Microsoft.AspNetCore.OpenApi</c> is added to the project
    /// alongside the Swagger generator.
    /// </summary>
    public static RouteGroupBuilder MapBasketGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/v1")
            .RequireAuthorization("Default")
            .WithTags("Baskets")
            // WithOpenApi() re-enabled — the
            // `Microsoft.AspNetCore.OpenApi` + `Swashbuckle.AspNetCore`
            // packages are now project dependencies, so the
            // generator picks up every endpoint in the group.
            .WithOpenApi();
}