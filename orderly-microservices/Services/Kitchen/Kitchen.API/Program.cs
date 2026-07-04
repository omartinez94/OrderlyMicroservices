using HealthChecks.UI.Client;
using Kitchen.API.Application;
using Kitchen.API.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("KitchenDB")!);

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