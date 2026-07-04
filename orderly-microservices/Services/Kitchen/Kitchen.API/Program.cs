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

// Health: EF Core + Postgres reachability. The RabbitMQ broker check is
// documented in KITCHEN_SERVICE_PLAN.md §12.5 but skipped here because
// the current AspNetCore.HealthChecks.Rabbitmq 8.0.x depends on
// RabbitMQ.Client 7.x while MassTransit 8.5.10 transitively pins 6.x —
// a follow-up commit should add the broker check once a compatible
// health-check package is published.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KitchenDbContext>(name: "kitchendb", tags: new[] { "db", "ready" });

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