using BuildingBlocks.Dev;
using BuildingBlocks.Exceptions.Handler;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Ordering.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        services.AddJwtAuthenticationWithDevFallback(
            environment,
            configuration,
            authority: configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
            audience: "OrderlyMicroservices");
        services.AddAuthorizationServices();

        services.AddHttpContextAccessor();
        services.AddCarter();

        // OpenAPI: machine-readable contract served at `/openapi/v1.json`. Built
        // on the in-box `Microsoft.AspNetCore.OpenApi` (ships with the .NET 10
        // SDK — no Swashbuckle dependency added). Every Carter module already
        // carries `.WithTags("Orders")` on its route group, so the generated
        // spec is grouped without any per-endpoint edit. Plan §6.5.
        services.AddOpenApi();

        // Dev-only runner handles for the Orderly.DevMCP.Server's
        // `trigger_scheduled_jobs` tool. The interfaces live in
        // Ordering.Infrastructure so the Infrastructure project can
        // implement them without a circular dep. Concrete registration
        // happens in Ordering.Infrastructure.DependencyInjection.
        // Here we just expose the endpoints; the runner-resolve is
        // scoped-per-request by the dev endpoint handler.

        services.AddExceptionHandler<CustomExceptionHandler>();

        // Health checks. The MSSQL check is tagged `"ready"` so it only fires
        // on the `/ready` readiness probe (Kubernetes readinessProbe /
        // compose `condition: service_healthy`). Phase 5 split: `/live` is
        // always green (process up); `/ready` aggregates every tag=`"ready"`
        // check (Postgres + future broker / outbox DLQ checks). The MSSQL
        // check is registered here (rather than the producer assembly) so the
        // Ordering.API host owns the contract — same pattern as Catalog.
        services.AddHealthChecks()
            .AddSqlServer(
                configuration.GetConnectionString("Database")!,
                tags: new[] { "ready" });

        return services;
    }

    public static WebApplication UseApiServices(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapCarter();

        // OpenAPI document endpoint. The in-box generator scans every endpoint
        // registered via MapCarter() and emits an OpenAPI 3.0 document at the
        // canonical `/openapi/v1.json` path. Tags come from the `.WithTags(...)`
        // already wired on each module's route group; summaries + descriptions
        // from the existing `.WithDescription(...)` calls.
        app.MapOpenApi();

        app.UseExceptionHandler(opts => { });

        // /live + /ready split (Phase 5). Mirrors the Catalog / Kitchen / Basket
        // shape: /live always returns 200 (no checks — process alive is enough);
        // /ready aggregates every tag=`"ready"` check via UIResponseWriter.
        // Pre-Phase-5 code mounted a single `/health` that ran every check
        // indiscriminately; Kubernetes would then 503 the liveness probe during
        // a transient MSSQL blip and restart the pod — a needless recovery
        // cycle. The split is the standard pattern.
        app.MapHealthChecks("/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        return app;
    }
}
