using HealthChecks.UI.Client;
using Kitchen.API.Application;
using Kitchen.API.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

// JSON: keep PascalCase (mirrors Catalog).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddJwtAuthentication(
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
    audience: "OrderlyMicroservices");

builder.Services.AddAuthorizationServices();

builder.Services.AddCarter();

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

// Health: EF Core + Postgres reachability + the RabbitMQ broker. The
// broker check (Phase E, Path E.1) uses AspNetCore.HealthChecks.Rabbitmq
// 8.0.2, whose RabbitMQ.Client dependency is `>= 6.8.1` — NuGet resolves
// it to the 7.2.1 transitive dep that MassTransit 8.5.10 pulls in. Tagged
// `broker, ready` so the ?tags=ready filter exposes it to readiness
// probes. Connection string reuses MessageBroker:Host (the AMQP URI the
// factory / production config already supply).
var rabbitConnectionString =
    builder.Configuration.GetValue<string>("MessageBroker:ConnectionString")
    ?? builder.Configuration.GetValue<string>("MessageBroker:Host")
    ?? "amqp://guest:guest@localhost:5672/";

builder.Services.AddHealthChecks()
    .AddDbContextCheck<KitchenDbContext>(name: "kitchendb", tags: new[] { "db", "ready" });

if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddRabbitMQ(
            rabbitConnectionString: rabbitConnectionString,
            name: "messagebroker",
            tags: new[] { "broker", "ready" });
}

var app = builder.Build();

// Apply pending EF Core migrations on startup. Postgres rarely has the
// MSSQL "database still recovering" race that Ordering works around with
// retry; the simple call mirrors Catalog's pattern. See
// KITCHEN_SERVICE_PLAN.md §8.4 for the future retry-helper work.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<KitchenDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapCarter();
app.MapHub<KitchenHub>("/hubs/kitchen");

app.UseExceptionHandler(options => { });

app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

app.Run();