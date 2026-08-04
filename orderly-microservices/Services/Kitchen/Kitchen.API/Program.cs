using BuildingBlocks.Dev;
using BuildingBlocks.Persistence;
using HealthChecks.UI.Client;
using Kitchen.API.Application;
using Kitchen.API.Infrastructure;
using Kitchen.API.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: traces + metrics + logs. Wired through the shared
// `BuildingBlocks.Observability.AddOrderlyOpenTelemetry` extension so
// the OTel pipeline shape is consistent across every Orderly service.
builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.Kitchen");
builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);

// JSON: keep PascalCase (mirrors Catalog).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddJwtAuthenticationWithDevFallback(
    builder.Environment,
    builder.Configuration,
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
    audience: "OrderlyMicroservices");

builder.Services.AddAuthorizationServices();

builder.Services.AddCarter();

// OpenAPI: machine-readable contract served at `/openapi/v1.json`. Built on
// the in-box `Microsoft.AspNetCore.OpenApi` (ships with the .NET 10 SDK —
// no Swashbuckle dependency added). Every Carter module already carries
// `.WithTags("Kitchen")` on its route definition, so the generated spec is
// grouped without any per-endpoint edit. Plan §6.5.
builder.Services.AddOpenApi();

// SignalR — single typed hub at /hubs/kitchen. JWT bearer is hoisted off
// the ?access_token= query string by the SignalR client; YARP forwards the
// WebSocket upgrade transparently (verified by route config in
// ApiGateway/YarpApiGateway/appsettings.json).
builder.Services.AddSignalR();

builder.Services
    .AddApplicationServices(builder.Configuration)
    .AddInfrastructureServices(builder.Configuration);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();

// Feature flags so Kitchen can participate in shared rollout gates
// (e.g. the OrderFullfilment kill switch lives on Ordering today; future
// flags like prep-time tracking land here).
builder.Services.AddFeatureManagement();

// Health: EF Core + Postgres reachability. MassTransit automatically adds
// a health check for the broker under the name "masstransit-bus" so we
// don't need a separate explicit RabbitMQ check.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KitchenDbContext>(name: "kitchendb", tags: new[] { "db", "ready" });

// Phase 2: replace the dev-only inline MigrateAsync with the shared
// MigratorHostedService. Schema application now runs at host startup
// (unconditional, not gated on IsDevelopment()) with exponential-backoff
// retry — survives Postgres cold-start during rolling restart.
builder.Services.Configure<MigratorHostedServiceOptions>(
    builder.Configuration.GetSection(MigratorHostedServiceOptions.SectionName));
builder.Services.AddHostedService<KitchenMigratorHostedService>();

var app = builder.Build();

// Phase 2: schema application is now owned by KitchenMigratorHostedService
// (registered above). The dev-only inline `await MigrateAsync()` block
// is removed to avoid double-applying the schema and to make production
// deploys also self-migrate.

app.UseAuthentication();
app.UseAuthorization();

app.MapCarter();
app.MapHub<KitchenHub>("/hubs/kitchen");

// OpenAPI document endpoint. The in-box generator scans every endpoint
// registered via MapCarter() and emits an OpenAPI 3.0 document at the
// canonical `/openapi/v1.json` path. Tags come from the `.WithTags(...)`
// already wired on each module's route definition; summaries + descriptions
// from any `.WithDescription(...)` calls. SignalR hub routes do not appear
// in the spec (MapHub is not a request-response endpoint).
app.MapOpenApi();

app.UseExceptionHandler(options => { });

// Phase 2: split /live + /ready. /live is unconditional green (process
// up); /ready is the readiness probe used by compose's
// `condition: service_healthy` chain and the HEALTHCHECK directive in
// every Dockerfile. The existing /health endpoint is kept for backwards
// compatibility (SignalR clients historically pinged it).
app.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        ResultStatusCodes =
        {
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        }
    });

app.Run();